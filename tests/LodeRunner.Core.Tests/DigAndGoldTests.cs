using LodeRunner.Core.Levels;
using LodeRunner.Core.Simulation;
using static LodeRunner.Core.Tests.TestWorld;

namespace LodeRunner.Core.Tests;

public class DigAndGoldTests
{
    private static readonly string Floor = new('#', 28);
    private static readonly string Concrete = new('@', 28);
    private static readonly InputState Right = new(Right: true);
    private static readonly InputState Left = new(Left: true);
    private static readonly InputState Up = new(Up: true);
    private static readonly InputState DigRight = new(DigRight: true);
    private static readonly InputState DigLeft = new(DigLeft: true);
    private static readonly InputState Idle = default;

    /// <summary>
    /// Pads to 16 rows. Adds a dummy gold in the top-right corner unless the map has its own,
    /// so the level parses without changing what the test is about.
    /// </summary>
    private static string[] Raw(params string[] bottomRows)
    {
        var rows = new List<string>();
        for (int i = 0; i < LevelParser.Rows - bottomRows.Length; i++) rows.Add("");
        rows.AddRange(bottomRows);
        if (!bottomRows.Any(r => r.Contains('$')))
            rows[0] = new string(' ', 27) + "$";
        return rows.ToArray();
    }

    // ---- gold ----

    [Fact]
    public void CollectsGoldWhenCentreEntersItsCell()
    {
        var w = CreateRaw(Raw("&$", Floor));
        Assert.Equal(1, w.GoldRemaining);

        Run(w, Right, 5); // X = 20: centre still inside column 0
        Assert.Equal(1, w.GoldRemaining);
        Run(w, Right, 1); // X = 24: centre crosses into column 1
        Assert.Equal(0, w.GoldRemaining);
        Assert.False(w.IsGold(1, 14));
    }

    [Fact]
    public void GoldPickupRaisesAnEventOnce()
    {
        var w = CreateRaw(Raw("&$", Floor));
        int ticks = TicksUntil(w, WorldEventKind.GoldCollected, Right);
        Assert.Equal(6, ticks);
        w.Tick(Right);
        Assert.DoesNotContain(w.Events, e => e.Kind == WorldEventKind.GoldCollected);
    }

    [Fact]
    public void RemainingGoldKeepsLaddersHidden()
    {
        var w = Create("&$", Floor); // dummy gold in the top-right corner stays
        Run(w, Right, 12);
        Assert.Equal(1, w.GoldRemaining);
        Assert.False(w.Grid.HiddenRevealed);
    }

    [Fact]
    public void LastGoldRevealsHiddenLadders()
    {
        var w = CreateRaw(Raw("   S", "   S", "&$ S", Floor));
        Assert.False(w.Grid.HiddenRevealed);
        Assert.Equal(TileType.Empty, w.Grid.Tile(3, 14));

        TicksUntil(w, WorldEventKind.LaddersRevealed, Right);
        Assert.True(w.Grid.HiddenRevealed);
        Assert.Equal(TileType.Ladder, w.Grid.Tile(3, 14));

        Run(w, Right, 24);
        Run(w, Up, 60); // slides to the ladder column, then climbs to the top
        Assert.Equal(11 * T, w.Player.Y);
    }

    [Fact]
    public void FallingThroughGoldCellCollectsIt()
    {
        var w = CreateRaw(Raw("&", "$", "", Floor));
        Run(w, Idle, 100);
        Assert.Equal(0, w.GoldRemaining);
    }

    // ---- dig rules ----

    [Fact]
    public void DigRightStartsAHoleBelowAndBesideThePlayer()
    {
        var w = CreateRaw(Raw("& ", Floor));
        w.Tick(DigRight);

        Assert.Equal(HoleState.Opening, w.Grid.Hole(1, 15).State);
        Assert.Equal(TileType.Empty, w.Grid.Tile(1, 15));
        Assert.Equal(TileType.Brick, w.Grid.Tile(0, 15));
        Assert.Equal(Pose.Dig, w.Player.Pose);
        Assert.Equal(Facing.Right, w.Player.Facing);
        Assert.Contains(w.Events, e => e.Kind == WorldEventKind.DigStarted && e.Cell == new Cell(1, 15));
    }

    [Fact]
    public void DigLeftStartsAHoleOnTheLeft()
    {
        var w = CreateRaw(Raw(" & ", Floor));
        w.Tick(DigLeft);
        Assert.Equal(HoleState.Opening, w.Grid.Hole(0, 15).State);
        Assert.Equal(Facing.Left, w.Player.Facing);
    }

    [Fact]
    public void PlayerIsFrozenWhileDigging()
    {
        var w = CreateRaw(Raw("& ", Floor));
        w.Tick(DigRight);
        Run(w, Right, Config.DigTicks - 2);
        Assert.Equal(0, w.Player.X);
        Assert.Equal(Pose.Dig, w.Player.Pose);
    }

    [Fact]
    public void PlayerMovesAgainOnceTheDigIsDone()
    {
        var w = CreateRaw(Raw("&  ", Floor));
        w.Tick(DigRight);
        Run(w, Left, 25); // against the level edge: stays at 0, but must have left the Dig pose
        Assert.NotEqual(Pose.Dig, w.Player.Pose);
        Assert.Equal(0, w.Player.DigTicksLeft);
    }

    [Fact]
    public void CannotDigTwoHolesAtOnce()
    {
        var w = CreateRaw(Raw(" & ", Floor));
        w.Tick(DigRight);
        w.Tick(DigLeft);
        Assert.Equal(HoleState.Intact, w.Grid.Hole(0, 15).State);
        Assert.Single(w.Grid.ActiveHoles);
    }

    [Fact]
    public void CannotDigConcrete()
    {
        var w = CreateRaw(Raw("& ", Concrete));
        w.Tick(DigRight);
        Assert.Empty(w.Grid.ActiveHoles);
        Assert.NotEqual(Pose.Dig, w.Player.Pose);
    }

    [Fact]
    public void CannotDigWhenBesideIsABrick()
    {
        var w = CreateRaw(Raw("&#", Floor));
        w.Tick(DigRight);
        Assert.Empty(w.Grid.ActiveHoles);
    }

    [Fact]
    public void CannotDigWhenBesideIsALadder()
    {
        var w = CreateRaw(Raw("&H", Floor));
        w.Tick(DigRight);
        Assert.Empty(w.Grid.ActiveHoles);
    }

    [Fact]
    public void CannotDigOntoGold()
    {
        var w = CreateRaw(Raw("&$", Floor));
        w.Tick(DigRight);
        Assert.Empty(w.Grid.ActiveHoles);
    }

    [Fact]
    public void CannotDigAtTheLevelEdge()
    {
        var w = CreateRaw(Raw("& ", Floor));
        w.Tick(DigLeft);
        Assert.Empty(w.Grid.ActiveHoles);
    }

    [Fact]
    public void CannotDigWhenNothingIsBelowTheTarget()
    {
        var w = CreateRaw(Raw("& ", "#"));
        w.Tick(DigRight);
        Assert.Empty(w.Grid.ActiveHoles);
    }

    [Fact]
    public void CannotDigWhileFalling()
    {
        var w = CreateRaw(Raw("&", "", " ", Floor));
        w.Tick(Idle);
        Assert.True(w.Player.Falling);
        w.Tick(DigRight);
        Assert.Empty(w.Grid.ActiveHoles);
    }

    [Fact]
    public void CannotDigWhileHangingOnARope()
    {
        var w = CreateRaw(Raw("&-", "###", Floor));
        Run(w, Right, 12);
        Assert.Equal(Pose.Hang, w.Player.Pose);
        w.Tick(DigRight);
        Assert.Empty(w.Grid.ActiveHoles);
    }

    [Fact]
    public void CannotDigWhileOnALadderOffTheGround()
    {
        var w = CreateRaw(Raw("  H", "  H#", "& H#", Floor));
        Run(w, Right, 24);
        Run(w, Up, 12); // one cell up: ladder below, nothing solid under the feet
        Assert.Equal(13 * T, w.Player.Y);
        w.Tick(DigRight);
        Assert.Empty(w.Grid.ActiveHoles);
    }

    [Fact]
    public void DigFromOffCentreSlidesBackToNearestColumnCentre()
    {
        var w = CreateRaw(Raw("&   ", Floor));
        Run(w, Right, 3); // X = 12: nearest column is 0
        w.Tick(DigRight);

        Assert.Equal(new[] { new Cell(1, 15) }, w.Grid.ActiveHoles);
        Run(w, Idle, Config.DigTicks);
        Assert.Equal(0, w.Player.X);
        Assert.Equal(Pose.Dig, w.Player.Pose);
    }

    [Fact]
    public void DigFromPastTheMidpointSlidesForwardToTheNextColumn()
    {
        var w = CreateRaw(Raw("&    ", Floor));
        Run(w, Right, 7); // X = 28: nearest column is 1
        w.Tick(DigRight);

        Assert.Equal(new[] { new Cell(2, 15) }, w.Grid.ActiveHoles);
        Run(w, Idle, Config.DigTicks);
        Assert.Equal(T, w.Player.X);
    }

    [Fact]
    public void DigAlignmentNeverOvershootsTheColumnCentre()
    {
        var w = CreateRaw(Raw("&    ", Floor));
        Run(w, Right, 6); // X = 24: exactly on the midpoint, rounds to column 1
        w.Tick(DigRight);
        for (int i = 0; i < Config.DigTicks; i++)
        {
            Assert.InRange(w.Player.X, 0, T);
            w.Tick(Idle);
        }
        Assert.Equal(T, w.Player.X);
    }

    [Fact]
    public void DigOffCentreStillNeedsAnIntactBrickBelowTheTarget()
    {
        var w = CreateRaw(Raw("&   ", Concrete));
        Run(w, Right, 3);
        w.Tick(DigRight);
        Assert.Empty(w.Grid.ActiveHoles);
    }

    // ---- hole lifecycle ----

    [Fact]
    public void HoleGoesThroughEveryPhaseAtTheConfiguredTimes()
    {
        var w = CreateRaw(Raw("& ", Floor));
        w.Tick(DigRight);

        Assert.Equal(w.Config.DigTicks, TicksUntil(w, WorldEventKind.HoleOpened));
        Assert.Equal(HoleState.Open, w.Grid.Hole(1, 15).State);

        // The player must be gone from the hole's column so nothing interferes.
        Assert.Equal(w.Config.HoleOpenTicks, TicksUntil(w, WorldEventKind.HoleRefilling));
        Assert.Equal(HoleState.Refilling, w.Grid.Hole(1, 15).State);
        Assert.Equal(TileType.Brick, w.Grid.Tile(1, 15));

        Assert.Equal(w.Config.RefillTicks, TicksUntil(w, WorldEventKind.HoleClosed));
        Assert.Equal(HoleState.Intact, w.Grid.Hole(1, 15).State);
        Assert.Empty(w.Grid.ActiveHoles);
    }

    [Fact]
    public void ClosedHoleCanBeDugAgain()
    {
        var w = CreateRaw(Raw("& ", Floor));
        w.Tick(DigRight);
        TicksUntil(w, WorldEventKind.HoleClosed);
        w.Tick(DigRight);
        Assert.Equal(HoleState.Opening, w.Grid.Hole(1, 15).State);
    }

    [Fact]
    public void RefillingHoleCannotBeDugAgain()
    {
        var w = CreateRaw(Raw("& ", Floor));
        w.Tick(DigRight);
        TicksUntil(w, WorldEventKind.HoleRefilling);
        w.Tick(DigRight);
        Assert.Equal(HoleState.Refilling, w.Grid.Hole(1, 15).State);
    }

    [Fact]
    public void OpenHoleLetsThePlayerFallIn()
    {
        var w = CreateRaw(Raw("&  ", Floor));
        w.Tick(DigRight);
        TicksUntil(w, WorldEventKind.HoleOpened);
        Run(w, Idle, 2); // finish the dig freeze

        Run(w, Right, 30);
        Assert.Equal(T, w.Player.X);
        Assert.Equal(15 * T, w.Player.Y);
        Assert.False(w.Player.Falling);
    }

    [Fact]
    public void PlayerCanWalkOverAClosedHole()
    {
        var w = CreateRaw(Raw("&  ", Floor));
        w.Tick(DigRight);
        TicksUntil(w, WorldEventKind.HoleClosed);
        Run(w, Right, 24);
        Assert.Equal(2 * T, w.Player.X);
        Assert.Equal(14 * T, w.Player.Y);
    }

    [Fact]
    public void RefillingHoleSupportsThePlayerAbove()
    {
        var w = CreateRaw(Raw("&  ", Floor));
        w.Tick(DigRight);
        TicksUntil(w, WorldEventKind.HoleRefilling);
        Run(w, Right, 24);
        Assert.Equal(2 * T, w.Player.X);
        Assert.Equal(14 * T, w.Player.Y);
    }

    [Fact]
    public void PlayerTrappedInAClosingHoleDies()
    {
        var w = CreateRaw(Raw("&  ", Floor));
        w.Tick(DigRight);
        TicksUntil(w, WorldEventKind.HoleOpened);
        Run(w, Idle, 2);
        Run(w, Right, 30); // fall into the hole

        Assert.Equal(GamePhase.Playing, w.Phase);
        TicksUntil(w, WorldEventKind.PlayerDied);
        Assert.Equal(GamePhase.Lost, w.Phase);
        Assert.Equal(HoleState.Intact, w.Grid.Hole(1, 15).State);
    }

    [Fact]
    public void WorldIsFrozenAfterTheEnd()
    {
        var w = CreateRaw(Raw("&  ", Floor));
        w.Tick(DigRight);
        TicksUntil(w, WorldEventKind.HoleOpened);
        Run(w, Idle, 2);
        Run(w, Right, 30);
        TicksUntil(w, WorldEventKind.PlayerDied);

        long ticks = w.TickCount;
        int x = w.Player.X;
        Run(w, Left, 10);
        Assert.Equal(x, w.Player.X);
        Assert.Equal(ticks, w.TickCount);
        Assert.Empty(w.Events);
    }

    [Fact]
    public void EventsAreOnlyValidForOneTick()
    {
        var w = CreateRaw(Raw("& ", Floor));
        w.Tick(DigRight);
        Assert.NotEmpty(w.Events);
        w.Tick(Idle);
        Assert.Empty(w.Events);
    }

    [Fact]
    public void HolesRefillInTheOrderTheyWereDug()
    {
        var w = CreateRaw(Raw(" & ", Floor));
        w.Tick(DigRight);
        Run(w, Idle, 19);
        w.Tick(DigLeft);
        Assert.Equal([new Cell(2, 15), new Cell(0, 15)], w.Grid.ActiveHoles);
    }

    [Fact]
    public void SameSeedAndInputGiveSameWorldWithHolesAndGold()
    {
        InputState[] script = [Right, DigRight, Idle, Left, DigLeft, Right, Right, Up, Idle];
        var a = CreateRaw(Raw(" & $", Floor));
        var b = CreateRaw(Raw(" & $", Floor));

        for (int round = 0; round < 40; round++)
        {
            foreach (var input in script)
            {
                Run(a, input, 9);
                Run(b, input, 9);
                Assert.Equal(Snapshot(a), Snapshot(b));
            }
        }
    }

    private static string Snapshot(GameWorld w)
    {
        var holes = string.Join(",", w.Grid.ActiveHoles.Select(h =>
        {
            var i = w.Grid.Hole(h.Col, h.Row);
            return $"{h.Col}.{h.Row}:{i.State}:{i.TicksLeft}";
        }));
        return $"{w.Player.X},{w.Player.Y},{w.Player.Pose},{w.Player.Falling},{w.Phase},{w.GoldRemaining},{holes}";
    }
}
