namespace LodeRunner.Core.Simulation;

/// <summary>
/// Anything that moves on the grid. Position is the top-left of the cell-sized box, in integer units
/// (<see cref="GameConfig.TileUnits"/> per cell, Y grows downward like grid rows).
/// Previous-tick position is kept so a renderer can interpolate.
/// </summary>
public abstract class Entity
{
    public int X { get; internal set; }
    public int Y { get; internal set; }
    public int PrevX { get; private set; }
    public int PrevY { get; private set; }
    public Facing Facing { get; internal set; } = Facing.Right;
    public Pose Pose { get; internal set; } = Pose.Stand;

    /// <summary>True when the entity changed position during the last tick.</summary>
    public bool Moved { get; internal set; }

    /// <summary>True while in free fall. Input is ignored until the entity lands or catches a ladder/rope.</summary>
    public bool Falling { get; internal set; }

    /// <summary>Column a stepped-off entity drifts toward while falling; -1 for none (ease to the nearest column).</summary>
    internal int FallTargetCol { get; set; } = -1;

    protected Entity(int x, int y)
    {
        X = PrevX = x;
        Y = PrevY = y;
    }

    internal void BeginTick()
    {
        PrevX = X;
        PrevY = Y;
    }

    /// <summary>Jump to a position without interpolating from the old one.</summary>
    internal void Teleport(int x, int y)
    {
        X = PrevX = x;
        Y = PrevY = y;
    }
}
