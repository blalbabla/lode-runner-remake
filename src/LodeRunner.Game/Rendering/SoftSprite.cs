using System.Numerics;
using Raylib_cs;

namespace LodeRunner.Game.Rendering;

/// <summary>
/// Soft round sprite (white disc with smooth falloff) for glows and dust. Drawn as a quad facing +Z, which is the
/// fixed camera direction of this game. Tint sets colour and opacity. Call between <see cref="BeginAdditive"/> or
/// <see cref="BeginAlpha"/> and <see cref="End"/> so that transparent pixels do not write depth.
/// </summary>
internal static class SoftSprite
{
    private const int Size = 64;

    private static Texture2D _tex;
    private static bool _ready;
    private static bool _additive;

    private static void Ensure()
    {
        if (_ready) return;
        var img = Raylib.GenImageColor(Size, Size, new Color(255, 255, 255, 0));
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float dx = (x + 0.5f - Size / 2f) / (Size / 2f);
                float dy = (y + 0.5f - Size / 2f) / (Size / 2f);
                float f = Math.Clamp(1f - MathF.Sqrt(dx * dx + dy * dy), 0f, 1f);
                float a = f * f * (3f - 2f * f);
                Raylib.ImageDrawPixel(ref img, x, y, new Color((byte)255, (byte)255, (byte)255, (byte)(a * 255f)));
            }
        }
        _tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(_tex, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(_tex, TextureWrap.Clamp);
        _ready = true;
    }

    /// <summary>Light-adding blend: glows, sparks, fire.</summary>
    public static void BeginAdditive()
    {
        Ensure();
        Rlgl.DisableDepthMask();
        Raylib.BeginBlendMode(BlendMode.Additive);
        _additive = true;
    }

    /// <summary>Ordinary alpha blend: dust, smoke.</summary>
    public static void BeginAlpha()
    {
        Ensure();
        Rlgl.DisableDepthMask();
        _additive = false;
    }

    public static void End()
    {
        Rlgl.SetTexture(0);
        if (_additive) Raylib.EndBlendMode();
        Rlgl.EnableDepthMask();
        _additive = false;
    }

    /// <param name="size">Diameter in world units.</param>
    public static void Draw(Vector3 pos, float size, Color tint)
    {
        if (tint.A == 0 || size <= 0f) return;
        float h = size / 2f;
        Rlgl.SetTexture(_tex.Id);
        Rlgl.Begin(DrawMode.Quads);
        Rlgl.Color4ub(tint.R, tint.G, tint.B, tint.A);
        Rlgl.TexCoord2f(0f, 1f); Rlgl.Vertex3f(pos.X - h, pos.Y - h, pos.Z);
        Rlgl.TexCoord2f(1f, 1f); Rlgl.Vertex3f(pos.X + h, pos.Y - h, pos.Z);
        Rlgl.TexCoord2f(1f, 0f); Rlgl.Vertex3f(pos.X + h, pos.Y + h, pos.Z);
        Rlgl.TexCoord2f(0f, 0f); Rlgl.Vertex3f(pos.X - h, pos.Y + h, pos.Z);
        Rlgl.End();
    }

    public static void Unload()
    {
        if (_ready) Raylib.UnloadTexture(_tex);
        _ready = false;
    }
}
