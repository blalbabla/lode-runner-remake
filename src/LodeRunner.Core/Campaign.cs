using LodeRunner.Core.Simulation;

namespace LodeRunner.Core;

/// <summary>
/// Progress through the level list: current level, lives and score. Pure bookkeeping, no simulation.
/// Scoring follows the original: 250 per gold, 75 per guard killed, 1500 per level, and a bonus life per level.
/// </summary>
public sealed class Campaign
{
    public const int StartLives = 5;
    public const int MaxLives = 99;
    public const int GoldScore = 250;
    public const int EnemyScore = 75;
    public const int LevelScore = 1500;

    public int LevelCount { get; }
    public int LevelIndex { get; private set; }
    public int Lives { get; private set; } = StartLives;
    public int Score { get; private set; }
    public int Deaths { get; private set; }

    private int _levelStartScore;
    private int _levelStartLives = StartLives;

    /// <summary>True once the last level was finished.</summary>
    public bool IsComplete { get; private set; }

    public bool IsGameOver => Lives <= 0;

    public Campaign(int levelCount, int startLevel = 0)
    {
        if (levelCount <= 0) throw new ArgumentOutOfRangeException(nameof(levelCount));
        LevelCount = levelCount;
        LevelIndex = Math.Clamp(startLevel, 0, levelCount - 1);
    }

    /// <summary>Applies the events of one tick: scoring, lost life, level bonus.</summary>
    public void Apply(IEnumerable<WorldEvent> events)
    {
        foreach (var ev in events)
        {
            switch (ev.Kind)
            {
                case WorldEventKind.GoldCollected:
                    Score += GoldScore;
                    break;
                case WorldEventKind.EnemyDied:
                    Score += EnemyScore;
                    break;
                case WorldEventKind.LevelWon:
                    Score += LevelScore;
                    Lives = Math.Min(MaxLives, Lives + 1);
                    break;
                case WorldEventKind.PlayerDied:
                    LoseLife();
                    break;
            }
        }
    }

    /// <summary>Costs a life. Also used when the player aborts a level on purpose.</summary>
    public void LoseLife()
    {
        if (Lives > 0) Lives--;
        Deaths++;
    }

    /// <summary>Moves to the next level; returns false (and marks the campaign complete) after the last one.</summary>
    public bool AdvanceLevel()
    {
        if (LevelIndex + 1 < LevelCount)
        {
            LevelIndex++;
            MarkLevelStart();
            return true;
        }
        IsComplete = true;
        return false;
    }

    /// <summary>Jumps to a level without touching lives or score (level select and dev keys).</summary>
    public void GoTo(int index)
    {
        LevelIndex = ((index % LevelCount) + LevelCount) % LevelCount;
        IsComplete = false;
        MarkLevelStart();
    }

    /// <summary>Starts a fresh run from <paramref name="startLevel"/> with full lives and no score.</summary>
    public void Restart(int startLevel = 0)
    {
        LevelIndex = Math.Clamp(startLevel, 0, LevelCount - 1);
        Lives = StartLives;
        Score = 0;
        Deaths = 0;
        IsComplete = false;
        MarkLevelStart();
    }

    /// <summary>Remembers score and lives as they are now, as the baseline of the level about to be played.</summary>
    public void MarkLevelStart()
    {
        _levelStartScore = Score;
        _levelStartLives = Lives;
    }

    /// <summary>Drops score earned on the current level (gold, kills, level bonus) so a replay cannot farm it.</summary>
    public void RevertScore() => Score = _levelStartScore;

    /// <summary>Takes back the life changes since the level began (the death, or the bonus life of a win).</summary>
    public void RevertLives() => Lives = _levelStartLives;
}
