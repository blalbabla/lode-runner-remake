using LodeRunner.Core.Levels;

namespace LodeRunner.Core.Tests;

public class ShippedLevelsTests
{
    private static string LevelsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LodeRunner.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "assets", "levels");
    }

    public static IEnumerable<object[]> LevelFiles() =>
        Directory.GetFiles(LevelsDir(), "*.txt").Select(f => new object[] { Path.GetFileName(f) });

    [Fact]
    public void ClassicCampaignShips() =>
        Assert.Equal(150, Directory.GetFiles(LevelsDir(), "*.txt").Length);

    [Theory]
    [MemberData(nameof(LevelFiles))]
    public void ShippedLevelParses(string fileName)
    {
        var level = LevelParser.Load(Path.Combine(LevelsDir(), fileName));

        Assert.True(level.Gold.Count > 0);
        // Exit: hidden ladders, or a ladder reaching row 0 / row 1 (the player steps from row 1 into row 0).
        bool hasHidden = false, ladderAtTop = false;
        for (int r = 0; r < level.Rows; r++)
            for (int c = 0; c < level.Cols; c++)
                hasHidden |= level[c, r] == TileType.HiddenLadder;
        for (int c = 0; c < level.Cols; c++)
            ladderAtTop |= level[c, 0] == TileType.Ladder || level[c, 1] == TileType.Ladder;
        Assert.True(hasHidden || ladderAtTop);
    }

    [Theory]
    [MemberData(nameof(LevelFiles))]
    public void ShippedLevelSimulatesWithEnemiesWithoutEscapingBounds(string fileName)
    {
        var level = LevelParser.Load(Path.Combine(LevelsDir(), fileName));
        var w = new Simulation.GameWorld(level, new GameConfig(), seed: 3);
        int t = w.Config.TileUnits;
        int initialGold = w.GoldRemaining;

        for (int i = 0; i < 3000 && w.Phase == Simulation.GamePhase.Playing; i++)
        {
            w.Tick(default);
            foreach (var e in w.Enemies)
            {
                Assert.InRange(e.X, 0, (level.Cols - 1) * t);
                Assert.InRange(e.Y, 0, (level.Rows - 1) * t);
            }
            Assert.InRange(w.GoldRemaining, 0, initialGold); // the idle player may fall onto gold
        }
    }
}
