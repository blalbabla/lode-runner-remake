using LodeRunner.Core.Levels;

namespace LodeRunner.Core.Simulation;

public enum GamePhase
{
    Playing,
    Lost,
    Won,
}

public enum WorldEventKind
{
    GoldCollected,
    LaddersRevealed,
    DigStarted,
    HoleOpened,
    HoleRefilling,
    HoleClosed,
    PlayerDied,
    LevelWon,
    EnemyTrapped,
    EnemyDied,
    EnemyRespawned,
    EnemyGoldTaken,
    EnemyGoldDropped,
}

/// <summary>The only channel from simulation to sound and effects. Valid for one tick.</summary>
public readonly record struct WorldEvent(WorldEventKind Kind, Cell Cell);
