namespace LodeRunner.Core;

/// <summary>
/// Tunable simulation parameters. All speeds are in units per tick and must divide TileUnits.
/// </summary>
public sealed record GameConfig
{
    public int TileUnits { get; init; } = 48;
    public int TickRate { get; init; } = 60;

    public int PlayerRunSpeed { get; init; } = 4;
    public int PlayerClimbSpeed { get; init; } = 4;
    public int PlayerRopeSpeed { get; init; } = 4;
    public int FallSpeed { get; init; } = 4;

    public int EnemyRunSpeed { get; init; } = 2;
    public int EnemyClimbSpeed { get; init; } = 2;
    public int EnemyFallSpeed { get; init; } = 2;

    public int DigTicks { get; init; } = 9;
    public int HoleOpenTicks { get; init; } = 430;
    public int RefillTicks { get; init; } = 24;

    public int EnemyTrapTicks { get; init; } = 120;
    public int EnemyClimbTicks { get; init; } = 36;
    public int EnemyRespawnTicks { get; init; } = 60;

    /// <summary>Guard decision logic. The simulation default is Smart; the game picks from user settings.</summary>
    public EnemyAiMode EnemyAi { get; init; } = EnemyAiMode.Smart;

    public int GoldDropMin { get; init; } = 300;
    public int GoldDropMax { get; init; } = 900;

    public IEnumerable<int> AllSpeeds()
    {
        yield return PlayerRunSpeed;
        yield return PlayerClimbSpeed;
        yield return PlayerRopeSpeed;
        yield return FallSpeed;
        yield return EnemyRunSpeed;
        yield return EnemyClimbSpeed;
        yield return EnemyFallSpeed;
    }

    /// <summary>True when every speed is positive and divides TileUnits.</summary>
    public bool AreSpeedsAligned() => AllSpeeds().All(s => s > 0 && TileUnits % s == 0);
}
