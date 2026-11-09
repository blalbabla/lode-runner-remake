using LodeRunner.Core.Levels;
using LodeRunner.Core.Simulation;

namespace LodeRunner.Core.Tests;

/// <summary>Builds small test worlds from ASCII maps anchored to the bottom of the level.</summary>
internal static class TestWorld
{
    public static readonly GameConfig Config = new();
    public static int T => Config.TileUnits;

    /// <summary>
    /// <paramref name="rows"/> become the bottom rows of a 28x16 level (the last row is row 15).
    /// A dummy gold is added in the top-right corner so the level is valid.
    /// </summary>
    public static GameWorld Create(int seed = 1, params string[] rows)
    {
        if (rows.Length > LevelParser.Rows - 1)
            throw new ArgumentException("Too many rows.");

        var all = new List<string>();
        for (int i = 0; i < LevelParser.Rows - rows.Length; i++)
            all.Add(i == 0 ? new string(' ', 27) + "$" : "");
        all.AddRange(rows);
        var level = LevelParser.Parse(string.Join("\n", all));
        return new GameWorld(level, Config, seed);
    }

    public static GameWorld Create(params string[] rows) => Create(1, rows);

    /// <summary>Same as <see cref="Create(int, string[])"/> but with a custom config and seed.</summary>
    public static GameWorld CreateWith(GameConfig config, int seed, params string[] rows)
    {
        var all = new List<string>();
        for (int i = 0; i < LevelParser.Rows - rows.Length; i++)
            all.Add(i == 0 ? new string(' ', 27) + "$" : "");
        all.AddRange(rows);
        return new GameWorld(LevelParser.Parse(string.Join("\n", all)), config, seed);
    }

    /// <summary>Builds a world from exactly 16 map rows, with no dummy gold added.</summary>
    public static GameWorld CreateRaw(params string[] rows)
    {
        if (rows.Length != LevelParser.Rows)
            throw new ArgumentException("Need exactly 16 rows.");
        return new GameWorld(LevelParser.Parse(string.Join("\n", rows)), Config, 1);
    }

    /// <summary>Ticks with <paramref name="input"/> until an event of <paramref name="kind"/> fires; returns ticks used.</summary>
    public static int TicksUntil(GameWorld w, WorldEventKind kind, InputState input = default, int limit = 2000)
    {
        for (int i = 1; i <= limit; i++)
        {
            w.Tick(input);
            if (w.Events.Any(e => e.Kind == kind)) return i;
        }
        throw new Xunit.Sdk.XunitException($"Event {kind} did not fire within {limit} ticks.");
    }

    public static void Run(GameWorld w, InputState input, int ticks)
    {
        for (int i = 0; i < ticks; i++) w.Tick(input);
    }

    public static int Col(Entity e) => (e.X + T / 2) / T;
    public static int Row(Entity e) => e.Y / T;
}
