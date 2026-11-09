namespace LodeRunner.Core;

/// <summary>Per-tick input. Movement flags are held state; dig flags are one-shot requests.</summary>
public readonly record struct InputState(
    bool Left = false,
    bool Right = false,
    bool Up = false,
    bool Down = false,
    bool DigLeft = false,
    bool DigRight = false);
