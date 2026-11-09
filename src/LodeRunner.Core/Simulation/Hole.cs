using LodeRunner.Core.Levels;

namespace LodeRunner.Core.Simulation;

/// <summary>Brick lifecycle: Intact -> Opening -> Open -> Refilling -> Intact.</summary>
public enum HoleState
{
    Intact,
    Opening,
    Open,
    Refilling,
}

public readonly record struct HoleInfo(HoleState State, int TicksLeft, int TicksTotal)
{
    /// <summary>Progress through the current phase, 0 at its start and 1 at its end.</summary>
    public float Progress => TicksTotal <= 0 ? 1f : 1f - (float)TicksLeft / TicksTotal;
}

/// <summary>A hole moved into <see cref="NewState"/> during the last tick. Intact means it closed.</summary>
internal readonly record struct HoleTransition(Cell Cell, HoleState NewState);
