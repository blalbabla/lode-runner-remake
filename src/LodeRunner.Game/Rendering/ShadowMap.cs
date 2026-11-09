using System.Numerics;
using Raylib_cs;

namespace LodeRunner.Game.Rendering;

/// <summary>
/// Directional-light shadow map: a depth-only framebuffer rendered from an orthographic camera placed along the
/// light direction. The scene is drawn into it once per frame; the lighting shader samples the depth texture.
/// </summary>
public sealed class ShadowMap : IDisposable
{
    private const float LightDistance = 30f;
    private const float NearPlane = 1f;
    private const float FarPlane = 60f;

    private readonly uint _fbo;
    private readonly uint _depthTex;
    private readonly RenderTexture2D _target;
    private double _savedNear = 0.05, _savedFar = 4000.0;

    public int Size { get; }
    public bool IsValid { get; }

    /// <summary>OpenGL id of the depth texture.</summary>
    public uint DepthTextureId => _depthTex;

    /// <summary>World-to-light-clip transform, valid after <see cref="Begin"/>.</summary>
    public Matrix4x4 LightViewProjection { get; private set; } = Matrix4x4.Identity;

    /// <summary>Depth bias per world unit, in the normalised depth range of the light projection.</summary>
    public static float DepthPerUnit => 1f / (FarPlane - NearPlane);

    public ShadowMap(int size = 2048)
    {
        Size = size;
        _fbo = Rlgl.LoadFramebuffer();
        if (_fbo == 0) return;

        _depthTex = Rlgl.LoadTextureDepth(size, size, false);
        Rlgl.FramebufferAttach(_fbo, _depthTex, FramebufferAttachType.Depth, FramebufferAttachTextureType.Texture2D, 0);
        IsValid = Rlgl.FramebufferComplete(_fbo);

        _target = new RenderTexture2D
        {
            Id = _fbo,
            Texture = new Texture2D { Id = 0, Width = size, Height = size, Mipmaps = 1 },
        };
    }

    /// <summary>Starts the depth pass for a level of the given size; draw the shadow casters, then call <see cref="End"/>.</summary>
    public void Begin(Vector3 lightDirection, int cols, int rows)
    {
        var center = new Vector3(cols / 2f, rows / 2f, 0f);
        var dir = Vector3.Normalize(lightDirection);
        var view = Matrix4x4.CreateLookAt(center + dir * LightDistance, center, Vector3.UnitY);

        // Fit the orthographic window around the level box as seen from the light.
        float half = 1f;
        foreach (float x in new[] { 0f, cols })
            foreach (float y in new[] { 0f, rows })
                foreach (float z in new[] { -0.7f, 0.6f })
                {
                    var v = Vector3.Transform(new Vector3(x, y, z), view);
                    half = MathF.Max(half, MathF.Max(MathF.Abs(v.X), MathF.Abs(v.Y)));
                }
        half += 0.5f;

        var cam = new Camera3D
        {
            Position = center + dir * LightDistance,
            Target = center,
            Up = Vector3.UnitY,
            FovY = half * 2f,
            Projection = CameraProjection.Orthographic,
        };

        Raylib.BeginTextureMode(_target);
        Raylib.ClearBackground(Color.White);
        _savedNear = Rlgl.GetCullDistanceNear();
        _savedFar = Rlgl.GetCullDistanceFar();
        Rlgl.SetClipPlanes(NearPlane, FarPlane);
        Raylib.BeginMode3D(cam);
        // Raylib's raymath stores matrices column-vector style (translation in m12..m14), which is the same memory
        // as System.Numerics read as a column-vector matrix; its MatrixMultiply(modelview, projection) is the
        // product P x MV in that notation, so the Numerics call takes the arguments in the opposite order.
        LightViewProjection = Matrix4x4.Multiply(Rlgl.GetMatrixProjection(), Rlgl.GetMatrixModelview());
    }

    public void End()
    {
        Raylib.EndMode3D();
        Rlgl.SetClipPlanes(_savedNear, _savedFar);
        Raylib.EndTextureMode();
    }

    public void Dispose()
    {
        if (_depthTex != 0) Rlgl.UnloadTexture(_depthTex);
        if (_fbo != 0) Rlgl.UnloadFramebuffer(_fbo);
    }
}
