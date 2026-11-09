using LodeRunner.Core.Simulation;

namespace LodeRunner.Core.Tests;

public class CampaignTests
{
    private static WorldEvent Ev(WorldEventKind k) => new(k, default);

    [Fact]
    public void StartsWithFiveLivesAndNoScore()
    {
        var c = new Campaign(3);
        Assert.Equal(5, c.Lives);
        Assert.Equal(0, c.Score);
        Assert.Equal(0, c.LevelIndex);
        Assert.False(c.IsGameOver);
    }

    [Fact]
    public void GoldAndEnemyKillsScore()
    {
        var c = new Campaign(3);
        c.Apply([Ev(WorldEventKind.GoldCollected), Ev(WorldEventKind.GoldCollected), Ev(WorldEventKind.EnemyDied)]);
        Assert.Equal(2 * 250 + 75, c.Score);
    }

    [Fact]
    public void EnemyGoldEventsDoNotScore()
    {
        var c = new Campaign(3);
        c.Apply([Ev(WorldEventKind.EnemyGoldTaken), Ev(WorldEventKind.EnemyTrapped), Ev(WorldEventKind.EnemyRespawned)]);
        Assert.Equal(0, c.Score);
    }

    [Fact]
    public void LevelWonScoresAndGivesBonusLife()
    {
        var c = new Campaign(3);
        c.Apply([Ev(WorldEventKind.LevelWon)]);
        Assert.Equal(1500, c.Score);
        Assert.Equal(6, c.Lives);
    }

    [Fact]
    public void BonusLifeIsCapped()
    {
        var c = new Campaign(3);
        for (int i = 0; i < 200; i++) c.Apply([Ev(WorldEventKind.LevelWon)]);
        Assert.Equal(Campaign.MaxLives, c.Lives);
    }

    [Fact]
    public void PlayerDeathCostsALifeAndFifthMeansGameOver()
    {
        var c = new Campaign(3);
        for (int i = 0; i < 4; i++) c.Apply([Ev(WorldEventKind.PlayerDied)]);
        Assert.Equal(1, c.Lives);
        Assert.False(c.IsGameOver);

        c.Apply([Ev(WorldEventKind.PlayerDied)]);
        Assert.Equal(0, c.Lives);
        Assert.True(c.IsGameOver);
        Assert.Equal(5, c.Deaths);
    }

    [Fact]
    public void LivesNeverGoNegative()
    {
        var c = new Campaign(3);
        for (int i = 0; i < 10; i++) c.LoseLife();
        Assert.Equal(0, c.Lives);
    }

    [Fact]
    public void ScoreSurvivesDeath()
    {
        var c = new Campaign(3);
        c.Apply([Ev(WorldEventKind.GoldCollected), Ev(WorldEventKind.PlayerDied)]);
        Assert.Equal(250, c.Score);
    }

    [Fact]
    public void AdvanceMovesThroughLevelsThenCompletes()
    {
        var c = new Campaign(2);
        Assert.True(c.AdvanceLevel());
        Assert.Equal(1, c.LevelIndex);
        Assert.False(c.IsComplete);

        Assert.False(c.AdvanceLevel());
        Assert.True(c.IsComplete);
        Assert.Equal(1, c.LevelIndex);
    }

    [Fact]
    public void GoToWrapsAndKeepsLivesAndScore()
    {
        var c = new Campaign(3);
        c.Apply([Ev(WorldEventKind.GoldCollected)]);
        c.GoTo(-1);
        Assert.Equal(2, c.LevelIndex);
        c.GoTo(3);
        Assert.Equal(0, c.LevelIndex);
        Assert.Equal(250, c.Score);
        Assert.Equal(5, c.Lives);
    }

    [Fact]
    public void RestartResetsRun()
    {
        var c = new Campaign(5);
        c.Apply([Ev(WorldEventKind.GoldCollected), Ev(WorldEventKind.PlayerDied)]);
        c.AdvanceLevel();
        c.Restart(2);
        Assert.Equal(2, c.LevelIndex);
        Assert.Equal(5, c.Lives);
        Assert.Equal(0, c.Score);
        Assert.Equal(0, c.Deaths);
    }

    [Fact]
    public void RevertScoreDropsOnlyScoreEarnedOnThisLevel()
    {
        var c = new Campaign(3);
        c.Apply([Ev(WorldEventKind.GoldCollected)]);
        c.AdvanceLevel(); // baseline now 250
        c.Apply([Ev(WorldEventKind.GoldCollected), Ev(WorldEventKind.EnemyDied)]);
        c.RevertScore();
        Assert.Equal(250, c.Score);
    }

    [Fact]
    public void RevertLivesTakesBackTheLevelWinBonusLife()
    {
        var c = new Campaign(3);
        c.Apply([Ev(WorldEventKind.LevelWon)]);
        Assert.Equal(6, c.Lives);
        c.RevertLives();
        Assert.Equal(5, c.Lives);
    }

    [Fact]
    public void MarkLevelStartAfterDeathKeepsLostLife()
    {
        var c = new Campaign(3);
        c.Apply([Ev(WorldEventKind.GoldCollected), Ev(WorldEventKind.PlayerDied)]);
        c.RevertScore();
        c.MarkLevelStart();
        Assert.Equal(4, c.Lives);
        Assert.Equal(0, c.Score);
        c.Apply([Ev(WorldEventKind.GoldCollected)]);
        c.RevertScore();
        Assert.Equal(0, c.Score);
        Assert.Equal(4, c.Lives);
    }

    [Fact]
    public void RestartResetsLevelBaseline()
    {
        var c = new Campaign(3);
        c.Apply([Ev(WorldEventKind.GoldCollected)]);
        c.Restart(1);
        c.RevertScore();
        c.RevertLives();
        Assert.Equal(0, c.Score);
        Assert.Equal(5, c.Lives);
    }
}
