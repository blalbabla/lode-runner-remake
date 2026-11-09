namespace LodeRunner.Core.Simulation;

public enum Facing
{
    Left,
    Right,
}

/// <summary>What the character is doing. Drives animation selection in the renderer.</summary>
public enum Pose
{
    Stand,
    Run,
    Climb,
    Hang,
    Fall,
    Dig,
}
