using LodeRunner.Core.Simulation;
using static LodeRunner.Core.Tests.TestWorld;

namespace LodeRunner.Core.Tests;

public class EnemyTests
{
    private static readonly string Floor = new('#', 28);
    private static readonly InputState Idle = default;
    private static readonly InputState DigRight = new(DigRight: true);
    private static readonly InputState DigLeft = new(DigLeft: true);

    private static bool Fired(GameWorld w, WorldEventKind kind) => w.Events.Any(e => e.Kind == kind);

    private static void RunUntil(GameWorld w, Func<bool> done, int limit = 3000)
    {
        for (int i = 0; i < limit; i++)
        {
            if (done()) return;
            w.Tick(Idle);
        }
        throw new Xunit.Sdk.XunitException("Condition not reached within tick limit.");
    }

    // ---- movement / chase ----

    [Fact]
    public void EnemyRunsAtConstantSpeedTowardPlayer()
    {
        var w = Create("&         0", Floor);
        var e = w.Enemies[0];
        int x0 = e.X;

        w.Tick(Idle);
        Assert.Equal(x0 - 2, e.X);
        for (int i = 0; i < 10; i++)
        {
            int before = e.X;
            w.Tick(Idle);
            Assert.Equal(before - 2, e.X);
        }
    }

    [Fact]
    public void EnemyRunsRightWhenPlayerIsToTheRight()
    {
        var w = Create("0         &", Floor);
        var e = w.Enemies[0];
        w.Tick(Idle);
        Assert.Equal(2, e.X);
        Assert.Equal(Facing.Right, e.Facing);
    }

    [Fact]
    public void EnemyAlwaysStopsOnCellBoundariesWhenTurning()
    {
        var w = Create("   &      0", Floor);
        var e = w.Enemies[0];
        for (int i = 0; i < 200; i++)
        {
            w.Tick(Idle);
            if (w.Phase != GamePhase.Playing) break;
            Assert.Equal(0, e.Y % T);
        }
    }

    [Fact]
    public void EnemyClimbsLadderToReachPlayerAbove()
    {
        var w = Create(
            "&     H     ",
            "######H#####",
            "      H     ",
            "  0   H     ",
            Floor);

        var e = w.Enemies[0];
        RunUntil(w, () => w.Phase != GamePhase.Playing || Row(e) <= 11, 400);
        Assert.Equal(GamePhase.Playing, w.Phase);
        Assert.Equal(11, Row(e));
    }

    [Fact]
    public void EnemyFallsOffLedgeToReachPlayerBelow()
    {
        var w = Create(
            "  0    ",
            "####   ",
            "       ",
            "     & ",
            Floor);

        var e = w.Enemies[0];
        RunUntil(w, () => w.Phase != GamePhase.Playing || Row(e) >= 14, 600);
        Assert.True(Row(e) >= 14 || w.Phase == GamePhase.Lost);
    }

    [Fact]
    public void EnemyFallsAtConstantSpeed()
    {
        var w = Create(
            "0",
            " ",
            " &",
            Floor);
        var e = w.Enemies[0];
        w.Tick(Idle);
        Assert.True(e.Falling);
        Assert.Equal(2, e.Y - e.PrevY);
        w.Tick(Idle);
        Assert.Equal(2, e.Y - e.PrevY);
    }

    [Fact]
    public void EnemyDoesNotPassThroughConcrete()
    {
        var w = Create("& @  0 ", Floor);
        var e = w.Enemies[0];
        Run(w, Idle, 300);
        Assert.True(e.X >= 3 * T);
    }

    [Fact]
    public void EnemiesDoNotOverlapHorizontally()
    {
        var w = Create("&         00", Floor);
        var a = w.Enemies[0];
        var b = w.Enemies[1];
        for (int i = 0; i < 40; i++)
        {
            w.Tick(Idle);
            Assert.True(Math.Abs(a.X - b.X) >= T, $"tick {i}: {a.X} vs {b.X}");
        }
    }

    // ---- contact ----

    [Fact]
    public void ContactKillsPlayer()
    {
        var w = Create("&    0", Floor);
        int ticks = TicksUntil(w, WorldEventKind.PlayerDied);
        Assert.Equal(GamePhase.Lost, w.Phase);
        Assert.InRange(ticks, 20, 150);
    }

    [Fact]
    public void EnemyOneCellAwayDoesNotKillYet()
    {
        var w = Create("&0", Floor);
        w.Tick(Idle);
        Assert.Equal(GamePhase.Playing, w.Phase);
    }

    // ---- traps ----

    private static GameWorld TrapWorld(GameConfig? cfg = null) =>
        CreateWith(cfg ?? Config, 1,
            "   &      0",
            Floor,
            Floor);

    private static void DigAndWaitForTrap(GameWorld w)
    {
        w.Tick(DigRight);
        RunUntil(w, () => w.Events.Any(e => e.Kind == WorldEventKind.EnemyTrapped), 600);
    }

    [Fact]
    public void EnemyFallsIntoOpenHoleAndIsTrapped()
    {
        var w = TrapWorld();
        DigAndWaitForTrap(w);

        var e = w.Enemies[0];
        Assert.Equal(EnemyState.Trapped, e.State);
        Assert.Equal(4, Col(e));
        Assert.Equal(14, Row(e));
        Assert.Equal(GamePhase.Playing, w.Phase);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void SecondEnemyWalksOverHeadOfTrappedEnemyToReachPlayer(int gap)
    {
        // First enemy at col 10, second `gap` cells behind it. The player digs right and then stands still
        // next to the hole. The second enemy must cross the trapped head and catch the player while the
        // first enemy is still stuck, instead of halting above the hole.
        string top = "   &      0" + new string(' ', gap - 1) + "0";
        var w = CreateWith(Config, 1, top, Floor, Floor);
        w.Tick(DigRight);

        RunUntil(w, () => w.Enemies[0].State == EnemyState.Trapped, 600);
        for (int i = 0; i < Config.EnemyTrapTicks - 5 && w.Phase == GamePhase.Playing; i++)
        {
            Assert.Equal(EnemyState.Trapped, w.Enemies[0].State);
            w.Tick(Idle);
        }

        Assert.Equal(GamePhase.Lost, w.Phase);
    }

    [Fact]
    public void EnemyIsStuckInHoleEvenWhenNothingIsBelowIt()
    {
        // Row 15 is empty: the player would drop through the hole, the enemy must not.
        var w = CreateWith(Config, 1,
            "   &      0",
            Floor,
            new string(' ', 28));
        DigAndWaitForTrap(w);

        var e = w.Enemies[0];
        Assert.Equal(EnemyState.Trapped, e.State);
        Assert.Equal(4, Col(e));
        Assert.Equal(14, Row(e));
        Assert.False(e.Falling);
        int y = e.Y;
        Run(w, Idle, 20);
        Assert.Equal(y, e.Y);
    }

    [Fact]
    public void TrappedEnemyStaysPut()
    {
        var w = TrapWorld();
        DigAndWaitForTrap(w);
        var e = w.Enemies[0];
        int x = e.X, y = e.Y;
        Run(w, Idle, 30);
        Assert.Equal((x, y), (e.X, e.Y));
        Assert.Equal(EnemyState.Trapped, e.State);
    }

    [Fact]
    public void TrappedEnemyClimbsOutAfterTrapAndClimbTime()
    {
        var w = TrapWorld();
        DigAndWaitForTrap(w);
        var e = w.Enemies[0];

        Run(w, Idle, Config.EnemyTrapTicks);
        Assert.Equal(EnemyState.ClimbingOut, e.State);

        int y0 = e.Y;
        w.Tick(Idle);
        Assert.True(e.Y < y0);

        Run(w, Idle, Config.EnemyClimbTicks);
        Assert.Equal(EnemyState.Active, e.State);
        Assert.Equal(13 * T, e.Y);
    }

    [Fact]
    public void ClimbingOutMovesUpSmoothlyByOneCell()
    {
        var w = TrapWorld();
        DigAndWaitForTrap(w);
        var e = w.Enemies[0];
        Run(w, Idle, Config.EnemyTrapTicks);

        int start = e.Y;
        int prev = start;
        while (e.State == EnemyState.ClimbingOut)
        {
            w.Tick(Idle);
            Assert.True(e.Y <= prev);
            prev = e.Y;
        }
        Assert.Equal(start - T, e.Y);
    }

    [Fact]
    public void ClosingHoleKillsTrappedEnemy()
    {
        var cfg = Config with { EnemyTrapTicks = 10000 };
        var w = TrapWorld(cfg);
        DigAndWaitForTrap(w);
        var e = w.Enemies[0];

        TicksUntil(w, WorldEventKind.EnemyDied, limit: 600);
        Assert.Equal(EnemyState.Dead, e.State);
        Assert.False(e.IsAlive);
    }

    [Fact]
    public void DeadEnemyRespawnsOnTopRowAfterDelay()
    {
        var cfg = Config with { EnemyTrapTicks = 10000 };
        var w = TrapWorld(cfg);
        DigAndWaitForTrap(w);
        var e = w.Enemies[0];
        TicksUntil(w, WorldEventKind.EnemyDied, limit: 600);

        int ticks = TicksUntil(w, WorldEventKind.EnemyRespawned, limit: 600);
        Assert.InRange(ticks, cfg.EnemyRespawnTicks - 1, cfg.EnemyRespawnTicks + 1);
        Assert.Equal(EnemyState.Active, e.State);
        Assert.Equal(0, Row(e));
        Assert.Equal(0, e.X % T);
    }

    [Fact]
    public void RespawnOnlyUsesFreeCellsAndRepeatsPerSeed()
    {
        static (int, int) Respawn(int seed)
        {
            var cfg = Config with { EnemyTrapTicks = 10000 };
            var w = CreateWith(cfg, seed, "   &      0", Floor, Floor);
            DigAndWaitForTrap(w);
            TicksUntil(w, WorldEventKind.EnemyDied, limit: 600);
            TicksUntil(w, WorldEventKind.EnemyRespawned, limit: 600);
            var e = w.Enemies[0];
            return (Col(e), Row(e));
        }

        Assert.Equal(Respawn(7), Respawn(7));
        Assert.Equal(0, Respawn(7).Item2);
    }

    [Fact]
    public void PlayerCanWalkOverTrappedEnemyHead()
    {
        var w = CreateWith(Config with { EnemyTrapTicks = 10000 }, 1,
            "   &      0",
            Floor,
            Floor);
        DigAndWaitForTrap(w);
        var e = w.Enemies[0];
        Assert.Equal(EnemyState.Trapped, e.State);

        // Player steps into the hole cell column from the left: lands on the trapped enemy's head.
        var right = new InputState(Right: true);
        Run(w, right, 12);
        Assert.Equal(GamePhase.Playing, w.Phase);
        Assert.False(w.Player.Falling);
        Assert.Equal(13, Row(w.Player));
    }

    [Fact]
    public void DigIsBlockedWhenEnemyOccupiesTargetCell()
    {
        var w = Create("&0", Floor, Floor);
        w.Tick(DigRight);
        Assert.DoesNotContain(w.Events, ev => ev.Kind == WorldEventKind.DigStarted);
        Assert.Empty(w.Grid.ActiveHoles);
    }

    // ---- gold ----

    [Fact]
    public void EnemyPicksUpGoldItWalksOver()
    {
        var w = Create("&     $ 0", Floor);
        Assert.Equal(2, w.GoldRemaining); // level gold + dummy gold
        TicksUntil(w, WorldEventKind.EnemyGoldTaken);
        Assert.True(w.Enemies[0].HasGold);
        Assert.False(w.IsGold(6, 14));
    }

    [Fact]
    public void CarriedGoldStillCountsAsRemaining()
    {
        var w = Create("&     $ 0", Floor);
        int before = w.GoldRemaining;
        TicksUntil(w, WorldEventKind.EnemyGoldTaken);
        Assert.Equal(before, w.GoldRemaining);
    }

    [Fact]
    public void EnemyDropsCarriedGoldAfterTimer()
    {
        var cfg = Config with { GoldDropMin = 20, GoldDropMax = 20 };
        var w = CreateWith(cfg, 1, "&                 $0", Floor);
        TicksUntil(w, WorldEventKind.EnemyGoldTaken);
        Assert.True(w.Enemies[0].HasGold);

        int ticks = TicksUntil(w, WorldEventKind.EnemyGoldDropped, limit: 200);
        Assert.InRange(ticks, 15, 60);
        Assert.False(w.Enemies[0].HasGold);
    }

    [Fact]
    public void EnemyDoesNotRePickDroppedGoldImmediately()
    {
        var cfg = Config with { GoldDropMin = 5, GoldDropMax = 5 };
        var w = CreateWith(cfg, 1, "&                 $0", Floor);
        TicksUntil(w, WorldEventKind.EnemyGoldTaken);
        TicksUntil(w, WorldEventKind.EnemyGoldDropped, limit: 200);
        w.Tick(Idle);
        Assert.False(w.Enemies[0].HasGold);
    }

    [Fact]
    public void EnemyLeavesGoldAboveHoleWhenItFallsIn()
    {
        var cfg = Config with { GoldDropMin = 100000, GoldDropMax = 100000 };
        var w = CreateWith(cfg, 1,
            "   &      $0",
            Floor,
            Floor);
        TicksUntil(w, WorldEventKind.EnemyGoldTaken);
        Assert.True(w.Enemies[0].HasGold);

        w.Tick(DigRight);
        TicksUntil(w, WorldEventKind.EnemyGoldDropped, limit: 800);
        Assert.Equal(EnemyState.Trapped, w.Enemies[0].State);
        Assert.False(w.Enemies[0].HasGold);
        Assert.True(w.IsGold(4, 13));
    }

    [Fact]
    public void GoldTakenByEnemyKeepsLaddersHidden()
    {
        var w = CreateRaw(
            "                           $",
            "", "", "", "", "", "", "", "", "", "", "", "",
            "&     $ 0",
            new string('#', 28),
            new string('#', 28));
        // Player collects nothing; enemy takes one gold; both still count.
        int before = w.GoldRemaining;
        TicksUntil(w, WorldEventKind.EnemyGoldTaken);
        Assert.Equal(before, w.GoldRemaining);
        Assert.False(w.Grid.HiddenRevealed);
    }

    // ---- determinism ----

    [Fact]
    public void SameSeedAndInputsGiveIdenticalState()
    {
        static string Trace(int seed)
        {
            var cfg = Config with { EnemyTrapTicks = 60 };
            var w = CreateWith(cfg, seed,
                "   &      0   0",
                "H#########H####",
                "H         H    ",
                "H  $      H  $ ",
                Floor);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 400; i++)
            {
                var input = (i % 90) switch
                {
                    < 10 => new InputState(Right: true),
                    < 20 => new InputState(Left: true),
                    < 22 => new InputState(DigRight: true),
                    _ => default,
                };
                w.Tick(input);
                sb.Append(w.Player.X).Append(',').Append(w.Player.Y);
                foreach (var e in w.Enemies)
                    sb.Append('|').Append(e.X).Append(',').Append(e.Y).Append(',').Append((int)e.State).Append(e.HasGold ? 'g' : '-');
                sb.Append(';');
            }
            return sb.ToString();
        }

        Assert.Equal(Trace(5), Trace(5));
    }

    // ---- pathfinder ----

    [Fact]
    public void PathfinderFindsRouteThroughLadder()
    {
        var w = Create(
            "     &      ",
            "######H#####",
            "      H     ",
            "0     H     ",
            Floor);
        // Enemy must first run right toward the ladder, not stand still.
        var e = w.Enemies[0];
        w.Tick(Idle);
        Assert.True(e.X > 0);
    }

    [Fact]
    public void PathfinderStopsAtNearestReachableCellWhenPlayerIsWalledOff()
    {
        var w = Create("&   @   0", Floor);
        var e = w.Enemies[0];
        Run(w, Idle, 300);
        Assert.Equal(GamePhase.Playing, w.Phase);
        Assert.Equal(5 * T, e.X);
    }
}
