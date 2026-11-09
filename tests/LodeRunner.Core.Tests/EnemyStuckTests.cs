using LodeRunner.Core.Levels;
using LodeRunner.Core.Simulation;

namespace LodeRunner.Core.Tests;

public class EnemyStuckTests
{
    private static GameWorld LoadShipped(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LodeRunner.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var level = LevelParser.Load(Path.Combine(dir!.FullName, "assets", "levels", file));
        return new GameWorld(level, new GameConfig(), 1);
    }

    /// <summary>Two enemies heading for the same ladder from opposite sides used to block each other forever.</summary>
    [Theory]
    [InlineData("001.txt")]
    [InlineData("002.txt")]
    [InlineData("003.txt")]
    public void ActiveEnemiesNeverFreezeInPlaceWhileChasing(string file)
    {
        var w = LoadShipped(file);
        var still = new int[w.Enemies.Count];
        var last = w.Enemies.Select(e => (e.X, e.Y)).ToArray();

        for (int t = 0; t < 4000 && w.Phase == GamePhase.Playing; t++)
        {
            w.Tick(default); // player idles, so enemies always have somewhere to go
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                var e = w.Enemies[i];
                still[i] = e.State == EnemyState.Active && (e.X, e.Y) == last[i] ? still[i] + 1 : 0;
                last[i] = (e.X, e.Y);
                Assert.True(still[i] < 120, $"{file}: enemy {i} frozen at ({e.X},{e.Y}) on tick {t}");
            }
        }
    }
}
