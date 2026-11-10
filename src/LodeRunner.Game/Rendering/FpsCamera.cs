using System;
using System.Numerics;
using Raylib_cs;
using LodeRunner.Game.Input;

namespace LodeRunner.Game.Rendering;

/// <summary>
/// First-person camera. Pure math, no state — the caller (Program.cs) owns yaw/pitch via FpsController.
///
/// Eye position: heroCenter + (0, EyeUp, 0.07) + forward * EyeForward
/// FOV: 78° (same as IntroCamera EyeFovY)
/// </summary>
public static class FpsCamera
{
    // Eye height relative to entity centre. 0.41 = top of head; 0.25 keeps the eye
    // well inside the cell so looking up with mouse pitch cannot clip through brick ceilings.
    private const float EyeUp      = 0.25f;
    private const float EyeForward = 0.13f;
    private static readonly Vector3 EyeBaseOffset = new(0f, EyeUp, 0.07f);

    /// <summary>Vertical FOV in degrees for the FPS camera.</summary>
    public const float FovY = 78f;

    /// <summary>
    /// Build a Camera3D placed at the hero's eyes, looking along the given yaw and pitch.
    /// </summary>
    /// <param name="heroCenter">Interpolated centre of the hero's cell box (from GridCoords.EntityCenter).</param>
    /// <param name="yaw">Camera yaw in radians. 0 = +X, π/2 = −Z (into the screen), π = −X.</param>
    /// <param name="pitch">Camera pitch in radians. Positive = looking up.</param>
    public static Camera3D Evaluate(Vector3 heroCenter, float yaw, float pitch)
    {
        float cp = MathF.Cos(pitch);
        // Forward vector: same convention as IntroCamera (yaw 0 = +X, yaw π/2 = −Z)
        var forward = new Vector3(
            cp * MathF.Cos(yaw),
            MathF.Sin(pitch),
            -cp * MathF.Sin(yaw));

        var eye = heroCenter + EyeBaseOffset + new Vector3(
            MathF.Cos(yaw) * EyeForward,
            0f,
            -MathF.Sin(yaw) * EyeForward);

        // Target placed 10 units ahead so camera sway applied by the caller stays imperceptible.
        return new Camera3D
        {
            Position   = eye,
            Target     = eye + forward * 10f,
            Up         = Vector3.UnitY,
            FovY       = FovY,
            Projection = CameraProjection.Perspective,
        };
    }

    /// <summary>Heading yaw for FpsHeading values, forwarded from FpsController for convenience.</summary>
    public static float YawFor(FpsHeading heading) => FpsController.HeadingToYaw(heading);
}
