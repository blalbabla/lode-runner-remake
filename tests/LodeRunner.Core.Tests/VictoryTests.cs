using LodeRunner.Core.Levels;
using LodeRunner.Core.Simulation;
using static LodeRunner.Core.Tests.TestWorld;

namespace LodeRunner.Core.Tests;

public class VictoryTests
{
    private static readonly InputState Up = new(Up: true);
    private static readonly InputState Right = new(Right: true);
    private static readonly string Floor = new('#', 28);

    /// <summary>
    /// Player at column 0 and gold at column 1 on row 14; a ladder in column 5 runs from row 14 to row 0.
    /// The upper part is made of <paramref name="upper"/> tiles ('H' or hidden 'S').
    /// </summary>
    private static string[] Tower(char upper, string goldRow = "&$   H")
    {
        var rows = new string[16];
        for (int r = 0; r < 14; r++) rows[r] = new string(' ', 5) + upper;
        rows[14] = goldRow;
        rows[15] = Floor;
        return rows;
    }

    /// <summary>Walks to the ladder column (X = 5 * TileUnits), then climbs until a phase change or the limit.</summary>
    private static void WalkAndClimb(GameWorld w, int climbTicks)
    {
        Run(w, Right, 5 * T / 4);
        for (int i = 0; i < climbTicks && w.Phase == GamePhase.Playing; i++) w.Tick(Up);
    }

    [Fact]
    public void ReachingTopRowWithGoldLeftDoesNotWin()
    {
        var w = CreateRaw(Tower('H', "&    H$"));
        Assert.Equal(1, w.GoldRemaining);

        WalkAndClimb(w, 14 * T / 4 + 10);

        Assert.Equal(0, w.Player.Y);
        Assert.Equal(GamePhase.Playing, w.Phase);
    }

    [Fact]
    public void ReachingTopRowAfterAllGoldWins()
    {
        var w = CreateRaw(Tower('S'));
        Assert.Equal(1, w.GoldRemaining);

        WalkAndClimb(w, 14 * T / 4 + 10);

        Assert.Equal(0, w.GoldRemaining);
        Assert.True(w.Grid.HiddenRevealed);
        Assert.Equal(GamePhase.Won, w.Phase);
        Assert.Equal(0, w.Player.Y);
    }

    [Fact]
    public void WinRaisesEventAndFreezesWorld()
    {
        var w = CreateRaw(Tower('S'));
        Run(w, Right, 5 * T / 4);
        int ticks = TicksUntil(w, WorldEventKind.LevelWon, Up, limit: 400);
        Assert.InRange(ticks, 14 * T / 4 - 4, 14 * T / 4 + 4);
        Assert.Equal(GamePhase.Won, w.Phase);

        int y = w.Player.Y;
        long tick = w.TickCount;
        w.Tick(Up);
        Assert.Empty(w.Events);
        Assert.Equal(y, w.Player.Y);
        Assert.Equal(tick, w.TickCount);
    }

    [Fact]
    public void PlayerBetweenRowsOneAndZeroIsNotAWinYet()
    {
        var w = CreateRaw(Tower('S'));
        Run(w, Right, 5 * T / 4);
        for (int i = 0; i < 400 && w.Player.Y > T / 2; i++) w.Tick(Up);

        Assert.True(w.Player.Y > 0);
        Assert.Equal(GamePhase.Playing, w.Phase);
    }

    [Fact]
    public void LastGoldOnTopRowWinsOnPickup()
    {
        var rows = new string[16];
        for (int r = 0; r < 16; r++) rows[r] = "";
        rows[0] = "&$";
        rows[1] = Floor;
        var w = CreateRaw(rows);

        TicksUntil(w, WorldEventKind.GoldCollected, Right);
        Assert.Equal(GamePhase.Won, w.Phase);
    }

    [Fact]
    public void LostStaysLost()
    {
        var w = Create("&    0", Floor);
        TicksUntil(w, WorldEventKind.PlayerDied);
        Assert.Equal(GamePhase.Lost, w.Phase);
        w.Tick(Up);
        Assert.Equal(GamePhase.Lost, w.Phase);
    }
}
