using LodeRunner.Core.Simulation;
using static LodeRunner.Core.Tests.TestWorld;

namespace LodeRunner.Core.Tests;

public class DeathCauseTests
{
    private static readonly string Floor = new('#', 28);
    private static readonly InputState Idle = default;
    private static readonly InputState Right = new(Right: true);
    private static readonly InputState DigRight = new(DigRight: true);

    [Fact]
    public void AliveHasNoCause()
    {
        var w = Create("&         0", Floor);
        Assert.Equal(DeathCause.None, w.DeathCause);
        Assert.Equal(-1, w.KillerId);
    }

    [Fact]
    public void GuardOnTheSameRowCatchesTheHero()
    {
        var w = Create("&    0", Floor);
        TicksUntil(w, WorldEventKind.PlayerDied);
        Assert.Equal(DeathCause.Caught, w.DeathCause);
        Assert.Equal(0, w.KillerId);
    }

    [Fact]
    public void ClosingHoleCrushesTheHero()
    {
        var w = CreateRaw(Raw("&  ", Floor));
        w.Tick(DigRight);
        TicksUntil(w, WorldEventKind.HoleOpened);
        Run(w, Idle, 2);
        Run(w, Right, 30);
        TicksUntil(w, WorldEventKind.PlayerDied);
        Assert.Equal(DeathCause.Crushed, w.DeathCause);
        Assert.Equal(-1, w.KillerId);
    }

    [Fact]
    public void GuardDroppingOntoTheHeroSquashesHim()
    {
        // Guard on a ledge, hero waiting in the gap below the ledge's end.
        var w = CreateRaw(Raw3(
            "0     ",
            "#####",
            "     &",
            Floor));
        // Hero waits under the ledge's edge; the guard walks off it and falls onto him.
        TicksUntil(w, WorldEventKind.PlayerDied);
        Assert.Equal(DeathCause.GuardFellOnHero, w.DeathCause);
    }

    [Fact]
    public void HeroDroppingOntoAGuardIsSwallowed()
    {
        // Hero walks off a high ledge while a guard runs in underneath him.
        // Use player-speed enemies so the guard is fast enough to reach the fall point.
        var fastEnemy = Config with { EnemyRunSpeed = 4, EnemyClimbSpeed = 4, EnemyFallSpeed = 4 };
        var w = CreateRaw(fastEnemy, Raw3(
            "&      ",
            "###    ",
            "       0",
            Floor));
        w.Tick(Idle);
        for (int i = 0; i < 400 && w.Phase == GamePhase.Playing; i++)
            w.Tick(i < 20 ? Idle : Right);
        Assert.Equal(GamePhase.Lost, w.Phase);
        Assert.Equal(DeathCause.HeroFellOnGuard, w.DeathCause);
    }

    private static string[] Raw3(string top, string mid, string low, string floor)
    {
        var rows = new string[16];
        for (int i = 0; i < 16; i++) rows[i] = i == 0 ? new string(' ', 27) + "$" : "";
        rows[12] = top;
        rows[13] = mid;
        rows[14] = low;
        rows[15] = floor;
        return rows;
    }

    private static string[] Raw(string row, string floor)
    {
        var rows = new string[16];
        for (int i = 0; i < 14; i++) rows[i] = i == 0 ? new string(' ', 27) + "$" : "";
        rows[14] = row;
        rows[15] = floor;
        return rows;
    }
}
