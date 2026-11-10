using LodeRunner.Core.Levels;

namespace LodeRunner.Core.Simulation;

/// <summary>
/// Deterministic simulation. Call <see cref="Tick"/> at a fixed rate; same level, config, seed
/// and input sequence always produce the same state.
///
/// Tick order (fixed, for reproducibility):
///  1. advance hole timers; closing holes crush whoever is inside
///  2. update the player (dig in progress, new dig request, or movement)
///  3. collect gold; reveal hidden ladders when none is left
///  3b. win if no gold is left and the player stands on row 0, aligned to it
///  4. update enemies in id order (respawn, trap/climb timers, chase, gold pickup/drop)
///  5. an active enemy touching the player kills them
/// </summary>
public sealed class GameWorld : IEntityProbe
{
    private readonly Speeds _playerSpeeds;
    private readonly Speeds _enemySpeeds;
    private readonly List<Enemy> _enemies = new();
    private readonly EnemyPathfinder _pathfinder;
    private readonly ClassicGuardAi _classicAi = new();
    private readonly int _contactDistanceSq;
    private readonly bool[,] _gold;
    private readonly List<WorldEvent> _events = new();
    private readonly List<HoleTransition> _transitions = new();

    public GameConfig Config { get; }
    public LevelData Level { get; }
    public Grid Grid { get; }
    public Player Player { get; }
    public IReadOnlyList<Enemy> Enemies => _enemies;
    public GamePhase Phase { get; private set; } = GamePhase.Playing;
    public int GoldRemaining { get; private set; }
    public long TickCount { get; private set; }

    /// <summary>Why the player died; <see cref="DeathCause.None"/> while alive. Purely informational.</summary>
    public DeathCause DeathCause { get; private set; }

    /// <summary>Id of the guard involved in the death, or -1 when none was (hole crush) or the player lives.</summary>
    public int KillerId { get; private set; } = -1;

    /// <summary>Guard decision logic. Starts as <see cref="GameConfig.EnemyAi"/>; may be switched at any time, guards decide only on cell boundaries.</summary>
    public EnemyAiMode EnemyAi { get; set; }

    /// <summary>Events raised during the last tick.</summary>
    public IReadOnlyList<WorldEvent> Events => _events;

    /// <summary>Seeded RNG for simulation use (enemy respawn, gold drop timing).</summary>
    internal Random Rng { get; }

    public GameWorld(LevelData level, GameConfig config, int seed)
    {
        if (!config.AreSpeedsAligned())
            throw new ArgumentException("Every speed must be positive and divide TileUnits.", nameof(config));

        Config = config;
        EnemyAi = config.EnemyAi;
        Level = level;
        Grid = new Grid(level, config.TileUnits);
        Rng = new Random(seed);
        _playerSpeeds = new Speeds(config.PlayerRunSpeed, config.PlayerClimbSpeed,
            config.PlayerRopeSpeed, config.FallSpeed);
        _enemySpeeds = new Speeds(config.EnemyRunSpeed, config.EnemyClimbSpeed,
            config.EnemyRunSpeed, config.EnemyFallSpeed);
        Player = new Player(level.PlayerStart.Col * config.TileUnits, level.PlayerStart.Row * config.TileUnits);

        _pathfinder = new EnemyPathfinder(level.Cols, level.Rows);
        int contact = config.TileUnits * 3 / 5;
        _contactDistanceSq = contact * contact;
        foreach (var s in level.EnemyStarts)
            _enemies.Add(new Enemy(_enemies.Count, s.Col * config.TileUnits, s.Row * config.TileUnits));

        _gold = new bool[level.Cols, level.Rows];
        foreach (var g in level.Gold)
        {
            if (!_gold[g.Col, g.Row]) GoldRemaining++;
            _gold[g.Col, g.Row] = true;
        }
    }

    public bool IsGold(int col, int row) => Grid.InBounds(col, row) && _gold[col, row];

    /// <summary>Cell containing the centre of the player's box.</summary>
    public Cell PlayerCell => CellOf(Player);

    public void Tick(InputState input)
    {
        _events.Clear();
        if (Phase != GamePhase.Playing) return;

        Player.BeginTick();
        foreach (var e in _enemies) e.BeginTick();

        AdvanceHoles();
        if (Phase != GamePhase.Playing)
        {
            TickCount++;
            return;
        }

        UpdatePlayer(input);
        CollectGold();

        // Reaching the top before enemies move wins even if one would have touched the player this tick.
        if (CheckWin())
        {
            TickCount++;
            return;
        }

        foreach (var e in _enemies) UpdateEnemy(e);
        CheckContact();

        TickCount++;
    }

    private void AdvanceHoles()
    {
        _transitions.Clear();
        Grid.AdvanceHoles(Config, _transitions);

        foreach (var tr in _transitions)
        {
            var kind = tr.NewState switch
            {
                HoleState.Open => WorldEventKind.HoleOpened,
                HoleState.Refilling => WorldEventKind.HoleRefilling,
                _ => WorldEventKind.HoleClosed,
            };
            _events.Add(new WorldEvent(kind, tr.Cell));

            // A closing hole kills whatever is still inside it.
            if (tr.NewState != HoleState.Intact) continue;

            foreach (var e in _enemies)
                if (e.IsAlive && CellOf(e) == tr.Cell) KillEnemy(e);

            if (Phase == GamePhase.Playing && CellOf(Player) == tr.Cell)
                KillPlayer(DeathCause.Crushed);
        }
    }

    private void UpdatePlayer(InputState input)
    {
        var p = Player;

        if (p.DigTicksLeft > 0)
        {
            p.DigTicksLeft--;
            AlignToColumn(p);
            p.Moved = p.X != p.PrevX;
            p.Pose = Pose.Dig;
            return;
        }

        if ((input.DigLeft && TryStartDig(-1)) || (input.DigRight && TryStartDig(1)))
        {
            AlignToColumn(p);
            p.Moved = p.X != p.PrevX;
            p.Pose = Pose.Dig;
            return;
        }

        int dx = (input.Right ? 1 : 0) - (input.Left ? 1 : 0);
        int dy = (input.Down ? 1 : 0) - (input.Up ? 1 : 0);
        Locomotion.Step(Grid, p, dx, dy, _playerSpeeds, this);
    }

    /// <summary>Slides the player toward the centre of the nearest column at run speed, never overshooting.</summary>
    private void AlignToColumn(Player p)
    {
        int t = Config.TileUnits;
        int target = (p.X + t / 2) / t * t;
        int diff = target - p.X;
        if (diff == 0) return;
        int step = Math.Min(Config.PlayerRunSpeed, Math.Abs(diff));
        p.X += diff > 0 ? step : -step;
    }

    /// <summary>
    /// Digging needs: not in free fall, an intact brick diagonally below-left or below-right of the player's
    /// current cell, and an empty (non-gold, non-enemy) cell directly beside the player on the same row.
    ///
    /// The player can dig while standing on a floor, climbing a ladder (any rung), or hanging on a rope.
    /// On a ladder or rope the player is not necessarily row-aligned, so the cell is derived from
    /// <see cref="CellOf"/> (centre-of-box rounding) rather than requiring Y % TileUnits == 0.
    /// </summary>
    private bool TryStartDig(int dir)
    {
        var p = Player;
        int t = Config.TileUnits;
        if (p.Falling) return false;

        bool onLadderOrRope = p.Pose is Pose.Climb or Pose.Hang;

        // On a floor the player must be row-aligned (original rule unchanged).
        // On a ladder/rope the player can dig from any rung position.
        if (!onLadderOrRope && p.Y % t != 0) return false;
        if (!onLadderOrRope && Locomotion.ShouldFall(Grid, p, this)) return false;

        // Determine the reference cell: for floor the row is p.Y/t; for ladder/rope use CellOf (rounded).
        int c = (p.X + t / 2) / t;
        int r = onLadderOrRope ? CellOf(p).Row : p.Y / t;

        // Rope: original game allows digging from a rope; no special restriction.
        // Ladder: allow at any rung including mid-ladder (original behaviour).

        int target = c + dir;
        if (!Grid.CanStartHole(target, r + 1)) return false;
        if (Grid.Tile(target, r) != TileType.Empty || Grid.IsTrap(target, r)) return false;
        if (IsGold(target, r)) return false;
        if (_enemies.Any(e => e.IsAlive && CellOf(e) == new Cell(target, r))) return false;

        p.Facing = dir < 0 ? Facing.Left : Facing.Right;
        p.DigTicksLeft = Config.DigTicks;
        var cell = new Cell(target, r + 1);
        Grid.StartHole(cell.Col, cell.Row, Config.DigTicks);
        _events.Add(new WorldEvent(WorldEventKind.DigStarted, cell));
        return true;
    }

    private void CollectGold()
    {
        var cell = CellOf(Player);
        if (!IsGold(cell.Col, cell.Row)) return;

        _gold[cell.Col, cell.Row] = false;
        GoldRemaining--;
        _events.Add(new WorldEvent(WorldEventKind.GoldCollected, cell));

        if (GoldRemaining == 0)
        {
            Grid.RevealHiddenLadders();
            _events.Add(new WorldEvent(WorldEventKind.LaddersRevealed, default));
        }
    }

    // ---- enemies ----

    private void UpdateEnemy(Enemy e)
    {
        int t = Config.TileUnits;
        e.Moved = false;

        switch (e.State)
        {
            case EnemyState.Dead:
                RespawnWhenReady(e);
                return;

            case EnemyState.Trapped:
                e.Pose = Pose.Stand;
                if (--e.StateTicksLeft <= 0)
                {
                    e.State = EnemyState.ClimbingOut;
                    e.ClimbStartY = e.Y;
                    e.ClimbTotal = e.StateTicksLeft = Math.Max(1, Config.EnemyClimbTicks);
                }
                return;

            case EnemyState.ClimbingOut:
                e.StateTicksLeft--;
                e.Y = e.ClimbStartY - t * (e.ClimbTotal - e.StateTicksLeft) / e.ClimbTotal;
                e.Moved = true;
                e.Pose = Pose.Climb;
                if (e.StateTicksLeft <= 0)
                {
                    e.State = EnemyState.Active;
                    e.PitCol = e.X / t;
                    e.PitRow = e.ClimbStartY / t;
                    e.DirX = e.DirY = 0;
                }
                return;
        }

        // Active.
        var cell = CellOf(e);
        if (e.PitCol >= 0 && (cell.Col != e.PitCol || e.Y != (e.PitRow - 1) * t))
            e.PitCol = -1;
        if (e.DropCol >= 0 && (cell.Col != e.DropCol || cell.Row != e.DropRow))
            e.DropCol = e.DropRow = -1;

        if (e.X % t == 0 && e.Y % t == 0)
        {
            var playerCell = CellOf(Player);
            Func<int, int, bool> support = (c, r) => (c == e.PitCol && r == e.PitRow - 1) || EnemyHeadBelow(e, c, r);
            var (dx, dy) = ChooseStep(e, cell, playerCell, support);

            // Same cell as the player but not touching yet: close the gap horizontally.
            if (dx == 0 && dy == 0 && cell == playerCell)
                dx = Math.Sign(Player.X - e.X);

            e.DirX = dx;
            e.DirY = dy;
        }

        int mx = e.DirX, my = e.DirY;
        if (mx != 0 && BlockedByEnemy(e, mx)) mx = 0;
        Locomotion.Step(Grid, e, mx, my, _enemySpeeds, this);

        // An enemy reaching an open hole is stuck in it whatever lies below, even mid-fall (unlike the player).
        cell = CellOf(e);
        if (e.Y % t == 0 &&
            Grid.Hole(cell.Col, cell.Row).State is HoleState.Opening or HoleState.Open)
        {
            TrapEnemy(e, cell);
            return;
        }

        HandleEnemyGold(e, cell);
    }

    /// <summary>
    /// Next move for an active guard standing on a cell boundary. Guards plan on a view where holes dug by the
    /// player still count as brick, so they run at the player across a hole and fall into it.
    /// Classic mode is greedy and can shuffle forever (e.g. up a ladder to the player's row, which has no
    /// foothold, and back down). When it tries to reverse into the cell it just left, the path search takes over
    /// for the next few cells.
    /// </summary>
    private (int Dx, int Dy) ChooseStep(Enemy e, Cell cell, Cell playerCell, Func<int, int, bool> support)
    {
        if (cell.Col != e.CurDecCol || cell.Row != e.CurDecRow)
        {
            e.PrevDecCol = e.CurDecCol;
            e.PrevDecRow = e.CurDecRow;
            e.CurDecCol = cell.Col;
            e.CurDecRow = cell.Row;
        }

        Grid.PlanningView = true;
        try
        {
            if (EnemyAi != EnemyAiMode.Classic)
                return _pathfinder.NextStep(Grid, cell, playerCell, support);

            if (e.SearchDecisionsLeft > 0)
            {
                e.SearchDecisionsLeft--;
                return _pathfinder.NextStep(Grid, cell, playerCell, support);
            }

            var step = _classicAi.NextStep(Grid, cell, playerCell, support);
            if (step != (0, 0) && cell.Col + step.Dx == e.PrevDecCol && cell.Row + step.Dy == e.PrevDecRow)
            {
                var searched = _pathfinder.NextStep(Grid, cell, playerCell, support);
                if (searched != (0, 0))
                {
                    e.SearchDecisionsLeft = 16;
                    return searched;
                }
            }
            return step;
        }
        finally
        {
            Grid.PlanningView = false;
        }
    }

    private void TrapEnemy(Enemy e, Cell cell)
    {
        e.State = EnemyState.Trapped;
        e.StateTicksLeft = Config.EnemyTrapTicks;
        e.Falling = false;
        e.DirX = e.DirY = 0;
        e.Pose = Pose.Stand;
        e.X = cell.Col * Config.TileUnits;
        _events.Add(new WorldEvent(WorldEventKind.EnemyTrapped, cell));

        // A falling enemy lets go of its gold above the pit.
        if (e.HasGold)
            TryDropGold(e, cell.Col, cell.Row - 1);
    }

    private void HandleEnemyGold(Enemy e, Cell cell)
    {
        int t = Config.TileUnits;

        if (!e.HasGold)
        {
            if (IsGold(cell.Col, cell.Row) && !(cell.Col == e.DropCol && cell.Row == e.DropRow))
            {
                _gold[cell.Col, cell.Row] = false;
                e.HasGold = true;
                e.GoldDropTicks = Rng.Next(Config.GoldDropMin, Config.GoldDropMax + 1);
                _events.Add(new WorldEvent(WorldEventKind.EnemyGoldTaken, cell));
            }
            return;
        }

        if (e.GoldDropTicks > 0) e.GoldDropTicks--;
        if (e.GoldDropTicks == 0 && !e.Falling && e.X % t == 0 && e.Y % t == 0)
            TryDropGold(e, cell.Col, cell.Row);
    }

    /// <summary>
    /// Gold lands only in an empty cell without gold. Carried gold still counts toward
    /// <see cref="GoldRemaining"/>, so the level stays winnable.
    /// </summary>
    private bool TryDropGold(Enemy e, int col, int row)
    {
        if (!Grid.InBounds(col, row) || Grid.Tile(col, row) != TileType.Empty || Grid.IsTrap(col, row) || _gold[col, row])
            return false;

        _gold[col, row] = true;
        e.HasGold = false;
        e.DropCol = col;
        e.DropRow = row;
        _events.Add(new WorldEvent(WorldEventKind.EnemyGoldDropped, new Cell(col, row)));
        return true;
    }

    private void KillEnemy(Enemy e)
    {
        _events.Add(new WorldEvent(WorldEventKind.EnemyDied, CellOf(e)));
        e.State = EnemyState.Dead;
        e.StateTicksLeft = Config.EnemyRespawnTicks;
        e.Falling = false;
        e.Moved = false;
        e.DirX = e.DirY = 0;
        e.PitCol = -1;
    }

    /// <summary>Reappears in a random free cell of the top row; keeps waiting if none is free.</summary>
    private void RespawnWhenReady(Enemy e)
    {
        if (e.StateTicksLeft > 0 && --e.StateTicksLeft > 0) return;

        int cols = Grid.Cols;
        int start = Rng.Next(cols);
        var player = CellOf(Player);
        for (int i = 0; i < cols; i++)
        {
            int c = (start + i) % cols;
            if (Grid.Tile(c, 0) != TileType.Empty || Grid.IsTrap(c, 0) || _gold[c, 0]) continue;
            if (player == new Cell(c, 0)) continue;
            if (_enemies.Any(o => o != e && o.IsAlive && CellOf(o) == new Cell(c, 0))) continue;

            e.Teleport(c * Config.TileUnits, 0);
            e.State = EnemyState.Active;
            e.Falling = false;
            e.Pose = Pose.Stand;
            _events.Add(new WorldEvent(WorldEventKind.EnemyRespawned, new Cell(c, 0)));
            return;
        }
    }

    /// <summary>
    /// Another enemy within one cell ahead on (nearly) the same row blocks horizontal movement.
    /// Two enemies walking toward each other would block each other forever, so the lower id has right of way.
    /// </summary>
    /// <summary>
    /// True if another live enemy (typically one stuck in a hole) stands directly under cell (c, r), holding up
    /// whoever is there. Must match <see cref="IEntityProbe.HasExtraSupport"/> or the pathfinder plans a fall
    /// that the physics never performs, leaving the enemy standing still.
    /// </summary>
    private bool EnemyHeadBelow(Enemy self, int c, int r)
    {
        int t = Config.TileUnits;
        foreach (var o in _enemies)
        {
            if (o == self || !o.IsAlive) continue;
            if (o.Y == (r + 1) * t && (o.X + t / 2) / t == c) return true;
        }
        return false;
    }

    private bool BlockedByEnemy(Enemy e, int dx)
    {
        int t = Config.TileUnits;
        foreach (var o in _enemies)
        {
            if (o == e || !o.IsAlive) continue;
            if (Math.Abs(o.Y - e.Y) >= t) continue;
            int ahead = (o.X - e.X) * dx;
            if (ahead <= 0 || ahead > t) continue;
            if (o.DirX == -dx && e.Id < o.Id) continue;
            return true;
        }
        return false;
    }

    private void CheckContact()
    {
        if (Phase != GamePhase.Playing) return;
        foreach (var e in _enemies)
        {
            if (e.State != EnemyState.Active) continue;
            long dx = e.X - Player.X, dy = e.Y - Player.Y;
            if (dx * dx + dy * dy < _contactDistanceSq)
            {
                // Who came from above? A guard in free fall that is clearly higher, or a falling player clearly higher.
                int quarter = Config.TileUnits / 4;
                var cause = dy <= -quarter && e.Falling ? DeathCause.GuardFellOnHero
                    : dy >= quarter && Player.Falling ? DeathCause.HeroFellOnGuard
                    : DeathCause.Caught;
                KillPlayer(cause, e.Id);
                return;
            }
        }
    }

    /// <summary>
    /// Support that is not terrain: an enemy directly below (head-walking, including enemies stuck in
    /// holes), or the pit an enemy has just climbed out of.
    /// </summary>
    bool IEntityProbe.HasExtraSupport(Entity entity)
    {
        int t = Config.TileUnits;

        if (entity is Enemy self && self.PitCol >= 0)
        {
            int c = (self.X + t / 2) / t;
            if (c == self.PitCol && self.Y == (self.PitRow - 1) * t) return true;
        }

        int col = (entity.X + t / 2) / t;
        foreach (var o in _enemies)
        {
            if (o == entity || !o.IsAlive) continue;
            if (o.Y == entity.Y + t && (o.X + t / 2) / t == col) return true;
        }
        return false;
    }

    private bool CheckWin()
    {
        if (GoldRemaining > 0 || Player.Y != 0) return false;

        Phase = GamePhase.Won;
        _events.Add(new WorldEvent(WorldEventKind.LevelWon, CellOf(Player)));
        return true;
    }

    private void KillPlayer(DeathCause cause, int killerId = -1)
    {
        Phase = GamePhase.Lost;
        DeathCause = cause;
        KillerId = killerId;
        _events.Add(new WorldEvent(WorldEventKind.PlayerDied, CellOf(Player)));
    }

    private Cell CellOf(Entity e)
    {
        int t = Config.TileUnits;
        return new Cell((e.X + t / 2) / t, (e.Y + t / 2) / t);
    }
}
