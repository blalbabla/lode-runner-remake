using System.Numerics;
using Raylib_cs;

namespace LodeRunner.Game.Ui;

/// <summary>
/// Retro pixel font (Press Start 2P, OFL) with a fallback to Raylib's default font. Sizes passed in are
/// "default font" sizes; the pixel font is wider, so they are scaled down to keep existing layouts intact.
/// </summary>
public static class UiFont
{
    private const float SizeScale = 0.62f;
    private static Font _font;
    private static bool _loaded;

    public static void Load(string path)
    {
        if (!File.Exists(path)) return;
        _font = Raylib.LoadFontEx(path, 48, null, 0);
        if (_font.Texture.Id == 0) return;
        Raylib.SetTextureFilter(_font.Texture, TextureFilter.Bilinear);
        _loaded = true;
    }

    public static void Unload()
    {
        if (_loaded) Raylib.UnloadFont(_font);
        _loaded = false;
    }

    private static float Px(int size) => MathF.Max(8f, MathF.Round(size * SizeScale));

    public static int Measure(string text, int size)
    {
        if (!_loaded) return Raylib.MeasureText(text, size);
        float px = Px(size);
        return (int)Raylib.MeasureTextEx(_font, text, px, 0f).X;
    }

    public static void Draw(string text, int x, int y, int size, Color color)
    {
        if (!_loaded)
        {
            Raylib.DrawText(text, x, y, size, color);
            return;
        }
        float px = Px(size);
        // Centre the smaller pixel glyphs inside the line box the default font would have used.
        float yOff = (size - px) * 0.5f;
        Raylib.DrawTextEx(_font, text, new Vector2(x, y + yOff), px, 0f, color);
    }

    public static void DrawCentered(string text, int centerX, int y, int size, Color color)
    {
        Draw(text, centerX - Measure(text, size) / 2, y, size, color);
    }

    /// <summary>Text with a dark drop shadow for readability over the 3D scene.</summary>
    public static void DrawShadow(string text, int x, int y, int size, Color color)
    {
        int off = Math.Max(1, size / 12);
        Draw(text, x + off, y + off, size, new Color(0, 0, 0, 200));
        Draw(text, x, y, size, color);
    }
}
