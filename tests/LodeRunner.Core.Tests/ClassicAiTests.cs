using LodeRunner.Core.Levels;
using LodeRunner.Core.Simulation;
using static LodeRunner.Core.Tests.TestWorld;

namespace LodeRunner.Core.Tests;

/// <summary>Classic (original-style, greedy) guard AI. Map legend: '&amp;' player, '0' guard, 'H' ladder.</summary>
public class ClassicAiTests
{
    private static readonly string Floor = new('#', 28);
    private static readonly InputState Idle = default;
    private static GameConfig Classic => Config with { EnemyAi = EnemyAiMode.Classic };

    [Fact]
    public void DefaultConfigStaysSmart() => Assert.Equal(EnemyAiMode.Smart, new GameConfig().EnemyAi);

    [Fact]
    public void WorldStartsWithConfiguredMode()
    {
        Assert.Equal(EnemyAiMode.Classic, CreateWith(Classic, 1, "&   0", Floor).EnemyAi);
        Assert.Equal(EnemyAiMode.Smart, Create("&   0", Floor).EnemyAi);
    }

    [Fact]
    public void RunsStraightAtPlayerOnTheSameRow()
    {
        var w = CreateWith(Classic, 1, "&         0", Floor);
        var e = w.Enemies[0];
        int x0 = e.X;
        w.Tick(Idle);
        Assert.Equal(x0 - 2, e.X);
        Assert.Equal(Facing.Left, e.Facing);
    }

    [Fact]
    public void ClimbsLadderToReachPlayerAbove()
    {
        // Player on the upper floor at the left, guard below at the right, ladder in the middle.
        var w = CreateWith(Classic, 1,
            "&                    ",
            "######H####################",
            "      H",
            "      H        0",
            Floor);
        RunUntilGuard(w, e => Row(e) == 11, 600);
        Assert.Equal(GamePhase.Playing, w.Phase);
    }

    [Fact]
    public void WalksOffLedgeToReachPlayerBelow()
    {
        // Player on the ground, guard on a short ledge above: it must walk off the edge and drop.
        var w = CreateWith(Classic, 1,
            "      0",
            "   #####",
            "",
            "",
            "&",
            Floor);
        RunUntilGuard(w, e => Row(e) == 14, 800);
    }

    [Fact]
    public void ClassicGuardCatchesPlayerOnOpenGround()
    {
        var w = CreateWith(Classic, 1, "&          0", Floor);
        for (int i = 0; i < 400 && w.Phase == GamePhase.Playing; i++) w.Tick(Idle);
        Assert.Equal(GamePhase.Lost, w.Phase);
    }

    [Fact]
    public void ClassicGuardDoesNotShuffleOnLadderWhenPlayerRowHasNoFoothold()
    {
        // The ladder on the right reaches the player's row (12) but nothing holds a guard up beside it there.
        // The real route is up to the walkway on row 9, left, then down the ladder at column 8.
        var w = CreateWith(Classic, 1,
            "        H           H",
            "        H###########H",
            "        H           H",
            "&       H           H",
            "########H#          H",
            "                   0H",
            Floor);
        var e = w.Enemies[0];
        int minCol = Col(e);
        for (int i = 0; i < 1500 && w.Phase == GamePhase.Playing; i++)
        {
            w.Tick(Idle);
            minCol = Math.Min(minCol, Col(e));
        }
        Assert.True(minCol < 10, $"Guard never left the right-hand ladder (min col {minCol}).");
    }

    [Fact]
    public void GuardsIgnorePlayerDugHolesWhenPlanning()
    {
        // A hole between player and guard on the same row: the guard still runs at the player and falls in.
        foreach (var mode in new[] { EnemyAiMode.Classic, EnemyAiMode.Smart })
        {
            var w = CreateWith(Config with { EnemyAi = mode }, 1,
                "&                    0",
                "######################",
                "",
                "",
                Floor);
            for (int i = 0; i < 200 && Col(w.Player) < 4; i++) w.Tick(new InputState(Right: true));
            Assert.Equal(4, Col(w.Player));
            w.Tick(new InputState(DigRight: true));
            Assert.True(w.Grid.Hole(5, 12).State is HoleState.Opening or HoleState.Open);

            bool trapped = false;
            for (int i = 0; i < 400 && !trapped && w.Phase == GamePhase.Playing; i++)
            {
                w.Tick(Idle);
                trapped = w.Enemies[0].State == EnemyState.Trapped;
            }
            Assert.True(trapped, $"{mode}: guard never fell into the hole.");
        }
    }

    [Fact]
    public void SwitchingModeMidRunKeepsTheSimulationWorking()
    {
        var w = CreateWith(Classic, 1, "&              0", Floor);
        Run(w, Idle, 20);
        w.EnemyAi = EnemyAiMode.Smart;
        int x = w.Enemies[0].X;
        Run(w, Idle, 5);
        Assert.True(w.Enemies[0].X < x);
    }

    [Fact]
    public void ClassicIsDeterministic()
    {
        string Snapshot()
        {
            var w = CreateWith(Classic, 7, "&    H   $   0 0", "#####H###########", "     H", "     H     0", Floor);
            Run(w, Idle, 600);
            return string.Join(";", w.Enemies.Select(e => $"{e.X},{e.Y},{e.State}"));
        }
        Assert.Equal(Snapshot(), Snapshot());
    }

    [Fact]
    public void EveryShippedLevelRunsInClassicMode()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "assets", "levels");
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.txt") : [];
        if (files.Length == 0) files = FindLevelFiles();
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var w = new GameWorld(LevelParser.Load(file), Classic, 1);
            for (int i = 0; i < 1200 && w.Phase == GamePhase.Playing; i++) w.Tick(Idle);
        }
    }

    private static string[] FindLevelFiles()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string p = Path.Combine(d.FullName, "assets", "levels");
            if (Directory.Exists(p)) return Directory.GetFiles(p, "*.txt");
        }
        return [];
    }

    private static void RunUntilGuard(GameWorld w, Func<Enemy, bool> done, int limit)
    {
        for (int i = 0; i < limit; i++)
        {
            if (done(w.Enemies[0])) return;
            if (w.Phase != GamePhase.Playing) return;
            w.Tick(Idle);
        }
        throw new Xunit.Sdk.XunitException($"Guard stuck at ({w.Enemies[0].X},{w.Enemies[0].Y}).");
    }
}
