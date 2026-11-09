using System.Text.Json;
using LodeRunner.Core;

namespace LodeRunner.Game.Settings;

/// <summary>Loads and saves the <see cref="HighScoreTable"/> as JSON next to the settings file.</summary>
public static class ScoreStore
{
    private sealed class FileDto
    {
        public List<ScoreEntry> Entries { get; set; } = new();
        public int GamesPlayed { get; set; }
        public int GamesCleared { get; set; }
        public int TotalDeaths { get; set; }
        public int BestLevel { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LodeRunner", "scores.json");

    public static HighScoreTable Load()
    {
        var table = new HighScoreTable();
        try
        {
            if (!File.Exists(FilePath)) return table;
            var dto = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(FilePath), JsonOptions);
            if (dto is null) return table;
            table.Load(dto.Entries.Where(e => e is not null), dto.GamesPlayed, dto.GamesCleared, dto.TotalDeaths, dto.BestLevel);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable file: start with an empty table.
        }
        return table;
    }

    public static void Save(HighScoreTable table)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var dto = new FileDto
            {
                Entries = table.Entries.ToList(),
                GamesPlayed = table.GamesPlayed,
                GamesCleared = table.GamesCleared,
                TotalDeaths = table.TotalDeaths,
                BestLevel = table.BestLevel,
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(dto, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Scores are a convenience; failing to save must not crash the game.
        }
    }
}
