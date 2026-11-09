using Raylib_cs;

namespace LodeRunner.Game.Settings;

/// <summary>Switches the window between fullscreen, borderless window and a normal window.</summary>
internal static class VideoManager
{
    public const int WindowedWidth = 1280;
    public const int WindowedHeight = 720;

    private static VideoMode _current = VideoMode.Windowed;

    public static void Apply(VideoMode mode)
    {
        if (mode == _current) return;

        // Leave the current mode first so toggles never stack.
        if (_current == VideoMode.Fullscreen && Raylib.IsWindowFullscreen())
            Raylib.ToggleFullscreen();
        else if (_current == VideoMode.Borderless && Raylib.IsWindowState(ConfigFlags.BorderlessWindowMode))
            Raylib.ToggleBorderlessWindowed();

        _current = mode;
        switch (mode)
        {
            case VideoMode.Windowed:
                Raylib.SetWindowSize(WindowedWidth, WindowedHeight);
                CenterWindow();
                break;
            case VideoMode.Fullscreen:
                Raylib.ToggleFullscreen();
                break;
            case VideoMode.Borderless:
                Raylib.ToggleBorderlessWindowed();
                break;
        }
    }

    private static void CenterWindow()
    {
        int m = Raylib.GetCurrentMonitor();
        var pos = Raylib.GetMonitorPosition(m);
        int x = (int)pos.X + (Raylib.GetMonitorWidth(m) - WindowedWidth) / 2;
        int y = (int)pos.Y + (Raylib.GetMonitorHeight(m) - WindowedHeight) / 2;
        Raylib.SetWindowPosition(x, y);
    }

    public static string Label(VideoMode mode) => mode switch
    {
        VideoMode.Fullscreen => "Fullscreen",
        VideoMode.Borderless => "Borderless window",
        _ => "Window",
    };
}
