namespace LodeRunner.Core.Tests;

public class HighScoreTableTests
{
    private static readonly DateTime Day = new(2026, 1, 1);
    private static ScoreEntry Run(int score, int level = 1, int deaths = 0, bool cleared = false) =>
        new(score, level, deaths, cleared, Day);

    [Fact]
    public void EmptyTableHasNoBest()
    {
        var t = new HighScoreTable();
        Assert.Equal(0, t.BestScore);
        Assert.Empty(t.Entries);
    }

    [Fact]
    public void EntriesAreSortedDescendingAndRankIsReturned()
    {
        var t = new HighScoreTable();
        Assert.Equal(0, t.Add(Run(1000)));
        Assert.Equal(0, t.Add(Run(3000)));
        Assert.Equal(1, t.Add(Run(2000)));
        Assert.Equal(new[] { 3000, 2000, 1000 }, t.Entries.Select(e => e.Score));
        Assert.Equal(3000, t.BestScore);
    }

    [Fact]
    public void TieRanksBelowTheOlderEntry()
    {
        var t = new HighScoreTable();
        t.Add(Run(500, level: 1));
        Assert.Equal(1, t.Add(Run(500, level: 2)));
        Assert.Equal(1, t.Entries[0].Level);
    }

    [Fact]
    public void ZeroScoreNeverQualifiesButStillCountsAsAGame()
    {
        var t = new HighScoreTable();
        Assert.Equal(-1, t.Add(Run(0, deaths: 5)));
        Assert.Empty(t.Entries);
        Assert.Equal(1, t.GamesPlayed);
        Assert.Equal(5, t.TotalDeaths);
    }

    [Fact]
    public void TableKeepsOnlyTheBestTen()
    {
        var t = new HighScoreTable();
        for (int i = 1; i <= 12; i++) t.Add(Run(i * 100));
        Assert.Equal(HighScoreTable.Capacity, t.Entries.Count);
        Assert.Equal(1200, t.Entries[0].Score);
        Assert.Equal(300, t.Entries[^1].Score);
    }

    [Fact]
    public void FullTableRejectsScoreBelowTheLast()
    {
        var t = new HighScoreTable();
        for (int i = 1; i <= 10; i++) t.Add(Run(i * 100));
        Assert.False(t.Qualifies(100));
        Assert.Equal(-1, t.Add(Run(100)));
        Assert.True(t.Qualifies(150));
    }

    [Fact]
    public void LifetimeStatsAccumulate()
    {
        var t = new HighScoreTable();
        t.Add(Run(1000, level: 4, deaths: 3));
        t.Add(Run(2000, level: 150, deaths: 9, cleared: true));
        t.Add(Run(10, level: 2, deaths: 5));
        Assert.Equal(3, t.GamesPlayed);
        Assert.Equal(1, t.GamesCleared);
        Assert.Equal(17, t.TotalDeaths);
        Assert.Equal(150, t.BestLevel);
    }

    [Fact]
    public void LoadSortsTrimsAndDropsBadEntries()
    {
        var t = new HighScoreTable();
        var saved = Enumerable.Range(0, 15).Select(i => Run(i * 10)).Reverse();
        t.Load(saved, gamesPlayed: -4, gamesCleared: 2, totalDeaths: 7, bestLevel: 9);
        Assert.Equal(HighScoreTable.Capacity, t.Entries.Count);
        Assert.Equal(140, t.Entries[0].Score);
        Assert.All(t.Entries, e => Assert.True(e.Score > 0));
        Assert.Equal(0, t.GamesPlayed);
        Assert.Equal(9, t.BestLevel);
    }
}
