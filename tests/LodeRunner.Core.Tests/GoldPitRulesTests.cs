using LodeRunner.Core.Simulation;
using static LodeRunner.Core.Tests.TestWorld;

namespace LodeRunner.Core.Tests;

/// <summary>Rules for gold carried by guards and for guards in pits. Map legend: '&amp;' player, '0' guard.</summary>
public class GoldPitRulesTests
{
    private static readonly string Floor = new('#', 28);
    private static readonly InputState Idle = default;

    private static GameConfig NeverDrop => Config with { GoldDropMin = 100000, GoldDropMax = 100000 };

    [Fact]
    public void GuardCarriesOnlyOneGoldAtATime()
    {
        // Guard starts at column 14 and walks left over the gold at 10, then the gold at 8.
        var w = CreateWith(NeverDrop, 1, "&       $ $   0", Floor);
        TicksUntil(w, WorldEventKind.EnemyGoldTaken);
        var taken = w.Events.First(e => e.Kind == WorldEventKind.EnemyGoldTaken).Cell;
        Assert.Equal(10, taken.Col);

        Run(w, Idle, 60); // walks across column 8
        Assert.True(w.Enemies[0].X < 8 * T, $"guard at x={w.Enemies[0].X}");
        Assert.True(w.Enemies[0].HasGold);
        Assert.True(w.IsGold(8, 14), "second gold must stay on the floor");
    }

    [Fact]
    public void GuardDefersItsDropUntilItLeavesTheRope()
    {
        // Timer expires while the guard is on the rope (columns 3..8); gold may only land in an empty cell.
        var cfg = Config with { GoldDropMin = 40, GoldDropMax = 40 };
        var w = CreateWith(cfg, 1, "&  ------ $  0", Floor);
        TicksUntil(w, WorldEventKind.EnemyGoldTaken);
        TicksUntil(w, WorldEventKind.EnemyGoldDropped, limit: 400);
        var drop = w.Events.First(e => e.Kind == WorldEventKind.EnemyGoldDropped).Cell;
        Assert.DoesNotContain(drop.Col, new[] { 3, 4, 5, 6, 7, 8 });
        Assert.Equal(GamePhase.Playing, w.Phase);
    }

    [Fact]
    public void DroppedGoldLandsInAnEmptyCellAndCountsAgain()
    {
        var cfg = Config with { GoldDropMin = 5, GoldDropMax = 5 };
        var w = CreateWith(cfg, 1, "&            $0", Floor);
        TicksUntil(w, WorldEventKind.EnemyGoldTaken);
        int remaining = w.GoldRemaining;
        TicksUntil(w, WorldEventKind.EnemyGoldDropped, limit: 300);
        var drop = w.Events.First(e => e.Kind == WorldEventKind.EnemyGoldDropped).Cell;

        Assert.True(w.IsGold(drop.Col, drop.Row));
        Assert.False(w.Enemies[0].HasGold);
        Assert.Equal(remaining, w.GoldRemaining); // carried and dropped gold both count as remaining
    }

    [Fact]
    public void GuardCrushedAfterDroppingGoldInPitLeavesGoldOnTheFloorAbove()
    {
        var cfg = NeverDrop with { EnemyTrapTicks = 100000 };
        var w = CreateWith(cfg, 1, "   &      $0", Floor, Floor);
        TicksUntil(w, WorldEventKind.EnemyGoldTaken);
        Assert.True(w.Enemies[0].HasGold);
        int remaining = w.GoldRemaining;

        w.Tick(new InputState(DigRight: true));
        TicksUntil(w, WorldEventKind.EnemyTrapped, limit: 800);
        TicksUntil(w, WorldEventKind.EnemyDied, limit: 800);
        Assert.True(w.IsGold(4, 13));
        Assert.Equal(remaining, w.GoldRemaining);
    }

    [Fact]
    public void DefaultTimingsLetAGuardEscapeBeforeTheHoleCloses()
    {
        Assert.True(Config.EnemyTrapTicks + Config.EnemyClimbTicks < Config.HoleOpenTicks);
    }

    [Fact]
    public void GuardStillInThePitWhenTheHoleClosesIsCrushed()
    {
        // Trap time longer than the hole stays open: the guard cannot get out in time.
        var cfg = Config with { EnemyTrapTicks = 10000 };
        var w = CreateWith(cfg, 1, "   &      0", Floor, Floor);
        w.Tick(new InputState(DigRight: true));
        TicksUntil(w, WorldEventKind.EnemyTrapped, limit: 800);
        TicksUntil(w, WorldEventKind.EnemyDied, limit: 800);
        Assert.Equal(EnemyState.Dead, w.Enemies[0].State);
    }
}
