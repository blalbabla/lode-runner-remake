namespace LodeRunner.Core;

/// <summary>One finished run.</summary>
public sealed record ScoreEntry(int Score, int Level, int Deaths, bool Cleared, DateTime Date);

/// <summary>
/// Sorted top-N list of finished runs plus lifetime counters. Pure data, no IO.
/// Ties keep the older entry ahead, so a new run must beat a score to outrank it.
/// </summary>
public sealed class HighScoreTable
{
    public const int Capacity = 10;

    private readonly List<ScoreEntry> _entries = new();

    public IReadOnlyList<ScoreEntry> Entries => _entries;

    public int GamesPlayed { get; private set; }
    public int GamesCleared { get; private set; }
    public int TotalDeaths { get; private set; }

    /// <summary>Best level number reached in any run (1-based), 0 if none.</summary>
    public int BestLevel { get; private set; }

    public int BestScore => _entries.Count > 0 ? _entries[0].Score : 0;

    /// <summary>Would this score make the table? Zero never does.</summary>
    public bool Qualifies(int score) =>
        score > 0 && (_entries.Count < Capacity || score > _entries[^1].Score);

    /// <summary>Records a finished run; returns its zero-based rank, or -1 when it did not make the table.</summary>
    public int Add(ScoreEntry entry)
    {
        GamesPlayed++;
        if (entry.Cleared) GamesCleared++;
        TotalDeaths += entry.Deaths;
        BestLevel = Math.Max(BestLevel, entry.Level);

        if (!Qualifies(entry.Score)) return -1;

        int rank = _entries.FindIndex(e => entry.Score > e.Score);
        if (rank < 0) rank = _entries.Count;
        _entries.Insert(rank, entry);
        if (_entries.Count > Capacity) _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        return rank;
    }

    /// <summary>Restores saved data; bad or surplus entries are dropped and the list is re-sorted.</summary>
    public void Load(IEnumerable<ScoreEntry> entries, int gamesPlayed, int gamesCleared, int totalDeaths, int bestLevel)
    {
        _entries.Clear();
        _entries.AddRange(entries.Where(e => e.Score > 0).OrderByDescending(e => e.Score).Take(Capacity));
        GamesPlayed = Math.Max(0, gamesPlayed);
        GamesCleared = Math.Max(0, gamesCleared);
        TotalDeaths = Math.Max(0, totalDeaths);
        BestLevel = Math.Max(0, bestLevel);
    }
}
