namespace LodeRunner.Core.Simulation;

/// <summary>How the player died. Lets a renderer pick a fitting death scene; it has no effect on the simulation.</summary>
public enum DeathCause
{
    None,

    /// <summary>A refilling hole closed over the player.</summary>
    Crushed,

    /// <summary>A guard reached the player on roughly the same level.</summary>
    Caught,

    /// <summary>A guard dropped onto the player from above.</summary>
    GuardFellOnHero,

    /// <summary>The player dropped onto a guard from above.</summary>
    HeroFellOnGuard,
}
