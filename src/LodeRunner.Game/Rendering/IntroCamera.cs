using System.Numerics;
using Raylib_cs;

namespace LodeRunner.Game.Rendering;

/// <summary>
/// Level intro shot: starts at the hero's eyes looking the way he faces, then pulls back along a sideways arc
/// while turning 90 degrees toward the screen, and ends exactly on the regular (classic) camera.
/// Pure math: the caller provides the final camera, so the last frame joins the gameplay view without a jump.
/// </summary>
public static class IntroCamera
{
    /// <summary>Vertical field of view at the hero's eyes; narrows to the gameplay value on the way out.</summary>
    private const float EyeFovY = 78f;

    /// <summary>Eye position relative to the entity centre (head centre is 0.41 up) plus a nudge ahead of the face.</summary>
    private static readonly Vector3 EyeOffset = new(0f, 0.41f, 0.07f);
    private const float EyeForward = 0.13f;

    /// <summary>Sideways swing of the pull-back path, as a fraction of the travelled distance.</summary>
    private const float ArcSwing = 0.16f;

    /// <summary>Camera distance from the head below which the hero model is hidden, so it never fills the lens.</summary>
    public const float HideHeroRadius = 1.0f;

    /// <summary>Smootherstep easing on [0, 1].</summary>
    public static float Ease(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    /// <param name="final">The regular gameplay camera the shot ends on.</param>
    /// <param name="heroCenter">Interpolated centre of the hero's cell box.</param>
    /// <param name="facing">+1 when the hero faces right (+X), -1 when he faces left.</param>
    /// <param name="t">Progress in [0, 1]; 0 is at the eyes, 1 is the final camera.</param>
    public static Camera3D Evaluate(Camera3D final, Vector3 heroCenter, float facing, float t)
    {
        float e = Ease(t);
        float side = facing >= 0f ? 1f : -1f;

        var eye = heroCenter + EyeOffset + new Vector3(side * EyeForward, 0f, 0f);

        // Heading as yaw/pitch: forward = (cos p cos y, sin p, -cos p sin y). Yaw 0 = +X, yaw pi/2 = -Z (into the screen).
        var endDir = Vector3.Normalize(final.Target - final.Position);
        float endYaw = MathF.Atan2(-endDir.Z, endDir.X);
        float endPitch = MathF.Asin(Math.Clamp(endDir.Y, -1f, 1f));
        float startYaw = side > 0f ? 0f : MathF.PI;

        float yaw = startYaw + (endYaw - startYaw) * e;
        float pitch = endPitch * e;

        float cp = MathF.Cos(pitch);
        var forward = new Vector3(cp * MathF.Cos(yaw), MathF.Sin(pitch), -cp * MathF.Sin(yaw));

        // Pull back to the gameplay position, bulging toward the side the hero faces.
        var position = Vector3.Lerp(eye, final.Position, e);
        float travelled = Vector3.Distance(eye, final.Position);
        position.X += side * ArcSwing * travelled * MathF.Sin(MathF.PI * e);

        // Look-at point as far away as the gameplay camera's: the caller adds a small camera sway to the position and a
        // quarter of it to the target, which only stays imperceptible when the target is far from the position.
        float lookDistance = Vector3.Distance(final.Position, final.Target);

        return new Camera3D
        {
            Position = position,
            Target = position + forward * lookDistance,
            Up = Vector3.UnitY,
            FovY = EyeFovY + (final.FovY - EyeFovY) * e,
            Projection = CameraProjection.Perspective,
        };
    }

    /// <summary>Head centre in world space, used to decide when the hero model should be hidden.</summary>
    public static Vector3 HeadCenter(Vector3 heroCenter) => heroCenter + new Vector3(0f, 0.41f, 0.07f);
}
