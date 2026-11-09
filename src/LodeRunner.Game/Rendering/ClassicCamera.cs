using System.Numerics;
using Raylib_cs;

namespace LodeRunner.Game.Rendering;

/// <summary>
/// Fixed side-view camera looking along -Z at the centre of the level.
/// Narrow fovy and large distance keep grid-edge distortion low.
/// The whole level (plus margin) always fits, also on narrow aspect ratios.
/// Set <c>orthographic</c> to true if perspective hurts readability.
/// </summary>
public sealed class ClassicCamera : ICameraMode
{
    public Camera3D Camera { get; }

    public ClassicCamera(int cols, int rows, float fovyDegrees = 30f, float margin = 2f,
        bool orthographic = false, float aspect = 16f / 9f)
    {
        float centerX = cols / 2f;
        float centerY = rows / 2f;
        float halfHeight = (rows + margin) / 2f;
        float halfWidth = (cols + margin) / 2f;
        float tanHalf = MathF.Tan(fovyDegrees * MathF.PI / 360f);

        // Distance needed for the height to fit, and for the width to fit at this aspect.
        float distance = MathF.Max(halfHeight / tanHalf, halfWidth / (tanHalf * aspect));
        float orthoHeight = MathF.Max(rows + margin, (cols + margin) / aspect);

        Camera = new Camera3D
        {
            Position = new Vector3(centerX, centerY, distance),
            Target = new Vector3(centerX, centerY, 0f),
            Up = Vector3.UnitY,
            FovY = orthographic ? orthoHeight : fovyDegrees,
            Projection = orthographic ? CameraProjection.Orthographic : CameraProjection.Perspective,
        };
    }
}
