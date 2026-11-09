using Raylib_cs;

namespace LodeRunner.Game.Ui;

/// <summary>
/// Startup screen with a progress bar. Each call draws one complete frame, which also keeps the window
/// responsive (event polling) while the main thread is busy creating game objects.
/// </summary>
public static class LoadingScreen
{
    private const string Title = "LODE RUNNER";

    private static readonly Color Gold = new(236, 190, 84, 255);
    private static readonly Color Dim = new(150, 158, 190, 255);

    /// <summary>Draws one frame of the loading screen. <paramref name="progress"/> is clamped to 0..1.</summary>
    public static void Show(float progress, string label)
    {
        progress = Math.Clamp(progress, 0f, 1f);

        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();
        float ui = sh / 720f;
        int cx = sw / 2;

        int barW = (int)MathF.Min(sw * 0.6f, 560f * ui);
        int barH = Math.Max(8, (int)(18 * ui));
        int barX = cx - barW / 2;
        int barY = (int)(sh * 0.58f);
        int border = Math.Max(2, (int)(2 * ui));

        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(16, 18, 28, 255));
        Raylib.DrawRectangleGradientV(0, 0, sw, sh, new Color(30, 36, 66, 255), new Color(8, 9, 16, 255));

        UiFont.DrawCentered(Title, cx, (int)(sh * 0.34f), (int)(64 * ui), Gold);

        Raylib.DrawRectangle(barX - border, barY - border, barW + border * 2, barH + border * 2, Dim);
        Raylib.DrawRectangle(barX, barY, barW, barH, new Color(10, 12, 20, 255));
        Raylib.DrawRectangle(barX, barY, (int)(barW * progress), barH, Gold);

        UiFont.DrawCentered(label, cx, barY + barH + (int)(22 * ui), (int)(22 * ui), Dim);
        Raylib.EndDrawing();
    }
}
