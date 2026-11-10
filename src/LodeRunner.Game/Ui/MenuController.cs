using System.Text.RegularExpressions;
using LodeRunner.Core;
using LodeRunner.Game.Audio;
using LodeRunner.Game.Settings;
using Raylib_cs;

namespace LodeRunner.Game.Ui;

internal enum MenuPage
{
    None,
    Main,
    LevelSelect,
    HighScores,
    Pause,
    Settings,
    Video,
    Controls,
    Gameplay,
    MusicMixer,
}

internal enum MenuCommand
{
    None,
    NewGame,
    Continue,
    StartLevel,
    Resume,
    RestartLevel,
    ToMainMenu,
    Exit,
    VolumeChanged,
    MusicVolumeChanged,
    EnemyAiChanged,
    GameSpeedChanged,
    GraphicsChanged,
    GameplayChanged,
    MusicMixerChanged,
}

/// <summary>
/// Main menu, pause menu and settings pages (video mode, key rebinding).
/// Menu navigation uses fixed keys (arrows, numpad, WASD, Enter, Esc) so a bad rebind can never lock the player out.
/// </summary>
internal sealed class MenuController
{
    private const int PadSlot = KeyBindings.Slots; // 4th column of the controls page is the gamepad button
    private const int ControlRows = 6; // one per GameAction; then "Reset" and "Back"

    private const int LevelCols = 10;
    private const int LevelRowsVisible = 5;

    private string[] MainItems => _settings.MaxUnlockedLevel > 0
        ? ["Continue", "New game", "Level select", "High scores", "Settings", "Exit"]
        : ["New game", "Level select", "High scores", "Settings", "Exit"];
    private static readonly string[] PauseItems = ["Resume", "Restart level", "Settings", "Main menu", "Exit"];
    private string[] SettingsItems => ["Video", "Controls", $"Sound: {_settings.Volume}%", $"Music: {_settings.MusicVolume}%", $"Enemy AI: {_settings.EnemyAi}", $"Game speed: {GameSpeedInfo.Label(_settings.Speed)}", $"Graphics: {_settings.Graphics}", "Gameplay", "Music mixer", "Back"];
    private static readonly VideoMode[] VideoModes = [VideoMode.Fullscreen, VideoMode.Borderless, VideoMode.Windowed];

    private readonly GameSettings _settings;
    private MenuPage _settingsParent = MenuPage.Main;
    private int _sel;
    private int _slot;
    private bool _listening;
    private bool _justOpened;

    private readonly int _levelCount;
    private int _levelSel;
    private int _levelTop; // first visible grid row

    private readonly HighScoreTable _scores;

    /// <summary>Rank of the run that just ended (highlighted on the high score page), or -1.</summary>
    public int HighlightRank { get; set; } = -1;

    public MenuController(GameSettings settings, int levelCount, HighScoreTable scores)
    {
        _settings = settings;
        _scores = scores;
        _levelCount = levelCount;
    }

    /// <summary>Zero-based level picked on the level select page (valid with <see cref="MenuCommand.StartLevel"/>).</summary>
    public int SelectedLevel => _levelSel;

    /// <summary>Called with the UI sound to play (move, select, back).</summary>
    public Action<Sfx>? Sound { get; set; }

    public MenuPage Page { get; private set; } = MenuPage.None;
    public bool Active => Page != MenuPage.None;

    public void Open(MenuPage page)
    {
        Page = page;
        _listening = false;
        _slot = 0;
        _sel = page == MenuPage.Video ? Array.IndexOf(VideoModes, _settings.Video) : 0;
        _justOpened = true;
        if (page == MenuPage.LevelSelect)
        {
            _levelSel = Math.Clamp(_settings.MaxUnlockedLevel, 0, _levelCount - 1);
            ScrollToSelection();
        }
    }

    public void Close() => Page = MenuPage.None;

    // ---- update ----

    public MenuCommand Update()
    {
        if (Page == MenuPage.None) return MenuCommand.None;
        if (_justOpened)
        {
            _justOpened = false;
            return MenuCommand.None;
        }

        var page = Page;
        int sel = _sel, levelSel = _levelSel, slot = _slot, volume = _settings.Volume, musicVolume = _settings.MusicVolume;
        bool listening = _listening;
        bool esc = Back();
        bool confirmKey = Confirm();

        var cmd = UpdateCore();

        Sfx? sfx = null;
        if (Page != page) sfx = esc ? Sfx.MenuBack : Sfx.MenuSelect;
        else if (listening) sfx = !_listening ? (esc ? Sfx.MenuBack : Sfx.MenuSelect) : null;
        else if (cmd == MenuCommand.Resume) sfx = Sfx.MenuBack;
        else if (cmd is not (MenuCommand.None or MenuCommand.Exit or MenuCommand.VolumeChanged or MenuCommand.MusicVolumeChanged)) sfx = Sfx.MenuSelect;
        else if (_sel != sel || _levelSel != levelSel || _slot != slot || _settings.Volume != volume || _settings.MusicVolume != musicVolume) sfx = Sfx.MenuMove;
        else if (_listening) sfx = Sfx.MenuSelect;
        else if (confirmKey && cmd == MenuCommand.None && !(Page == MenuPage.LevelSelect && _levelSel > _settings.MaxUnlockedLevel))
            sfx = Sfx.MenuSelect;

        if (sfx is { } s) Sound?.Invoke(s);
        return cmd;
    }

    private MenuCommand UpdateCore()
    {
        if (_listening) { UpdateListening(); return MenuCommand.None; }

        // The key queue is only read while rebinding; discard stale presses (including the Enter that starts it).
        while (Raylib.GetKeyPressed() != 0) { }

        if (Page == MenuPage.LevelSelect) return UpdateLevelSelect();
        if (Page == MenuPage.HighScores)
        {
            if (Back() || Confirm()) { HighlightRank = -1; Open(MenuPage.Main); }
            return MenuCommand.None;
        }

        int count = ItemCount();
        if (Hit(KeyboardKey.Up, KeyboardKey.Kp8, KeyboardKey.W) || PadInput.NavUp) _sel = (_sel + count - 1) % count;
        if (Hit(KeyboardKey.Down, KeyboardKey.Kp2, KeyboardKey.S) || PadInput.NavDown) _sel = (_sel + 1) % count;

        bool left = Hit(KeyboardKey.Left, KeyboardKey.Kp4, KeyboardKey.A) || PadInput.NavLeft;
        bool right = Hit(KeyboardKey.Right, KeyboardKey.Kp6, KeyboardKey.D) || PadInput.NavRight;
        bool confirm = Confirm();
        bool back = Back();

        return Page switch
        {
            MenuPage.Main => UpdateMain(confirm),
            MenuPage.Pause => UpdatePause(confirm, back),
            MenuPage.Settings => UpdateSettings(confirm, back, left, right),
            MenuPage.Video => UpdateVideo(confirm, back),
            MenuPage.Controls => UpdateControls(confirm, back, left, right),
            MenuPage.Gameplay => UpdateGameplay(confirm, back, left, right),
            MenuPage.MusicMixer => UpdateMusicMixer(confirm, back, left, right),
            _ => MenuCommand.None,
        };
    }

    private MenuCommand UpdateMain(bool confirm)
    {
        if (!confirm) return MenuCommand.None;
        switch (MainItems[_sel])
        {
            case "Continue": return MenuCommand.Continue;
            case "New game": return MenuCommand.NewGame;
            case "Level select": Open(MenuPage.LevelSelect); break;
            case "High scores": Open(MenuPage.HighScores); break;
            case "Settings": OpenSettings(MenuPage.Main); break;
            default: return MenuCommand.Exit;
        }
        return MenuCommand.None;
    }

    private MenuCommand UpdateLevelSelect()
    {
        int max = _settings.MaxUnlockedLevel;
        if (Hit(KeyboardKey.Left, KeyboardKey.Kp4, KeyboardKey.A) || PadInput.NavLeft) _levelSel = Math.Max(0, _levelSel - 1);
        if (Hit(KeyboardKey.Right, KeyboardKey.Kp6, KeyboardKey.D) || PadInput.NavRight) _levelSel = Math.Min(_levelCount - 1, _levelSel + 1);
        if (Hit(KeyboardKey.Up, KeyboardKey.Kp8, KeyboardKey.W) || PadInput.NavUp) _levelSel = Math.Max(0, _levelSel - LevelCols);
        if (Hit(KeyboardKey.Down, KeyboardKey.Kp2, KeyboardKey.S) || PadInput.NavDown) _levelSel = Math.Min(_levelCount - 1, _levelSel + LevelCols);
        if (Hit(KeyboardKey.PageUp) || PadInput.PagePrev) _levelSel = Math.Max(0, _levelSel - LevelCols * LevelRowsVisible);
        if (Hit(KeyboardKey.PageDown) || PadInput.PageNext) _levelSel = Math.Min(_levelCount - 1, _levelSel + LevelCols * LevelRowsVisible);
        if (Raylib.IsKeyPressed(KeyboardKey.Home)) _levelSel = 0;
        if (Raylib.IsKeyPressed(KeyboardKey.End)) _levelSel = Math.Min(max, _levelCount - 1);
        ScrollToSelection();

        if (Back()) { Open(MenuPage.Main); return MenuCommand.None; }

        bool confirm = Confirm();
        return confirm && _levelSel <= max ? MenuCommand.StartLevel : MenuCommand.None;
    }

    private void ScrollToSelection()
    {
        int row = _levelSel / LevelCols;
        if (row < _levelTop) _levelTop = row;
        if (row >= _levelTop + LevelRowsVisible) _levelTop = row - LevelRowsVisible + 1;
    }

    private MenuCommand UpdatePause(bool confirm, bool back)
    {
        if (back) return MenuCommand.Resume;
        if (!confirm) return MenuCommand.None;
        switch (_sel)
        {
            case 0: return MenuCommand.Resume;
            case 1: return MenuCommand.RestartLevel;
            case 2: OpenSettings(MenuPage.Pause); break;
            case 3: return MenuCommand.ToMainMenu;
            default: return MenuCommand.Exit;
        }
        return MenuCommand.None;
    }

    private MenuCommand UpdateSettings(bool confirm, bool back, bool left, bool right)
    {
        if (back) { Open(_settingsParent); return MenuCommand.None; }

        if (_sel == 2 && (left || right))
        {
            _settings.Volume = Math.Clamp(_settings.Volume + (right ? 10 : -10), 0, 100);
            _settings.Save();
            return MenuCommand.VolumeChanged;
        }

        if (_sel == 3 && (left || right))
        {
            _settings.MusicVolume = Math.Clamp(_settings.MusicVolume + (right ? 10 : -10), 0, 100);
            _settings.Save();
            return MenuCommand.MusicVolumeChanged;
        }

        if (_sel == 4 && (left || right || confirm))
        {
            _settings.EnemyAi = _settings.EnemyAi == EnemyAiMode.Classic ? EnemyAiMode.Smart : EnemyAiMode.Classic;
            _settings.Save();
            return MenuCommand.EnemyAiChanged;
        }

        if (_sel == 5 && (left || right || confirm))
        {
            int n = Enum.GetValues<GameSpeed>().Length;
            int step = left ? -1 : 1;
            _settings.Speed = (GameSpeed)(((int)_settings.Speed + step + n) % n);
            _settings.Save();
            return MenuCommand.GameSpeedChanged;
        }

        if (_sel == 6 && (left || right || confirm))
        {
            _settings.Graphics = _settings.Graphics == GraphicsQuality.High ? GraphicsQuality.Low : GraphicsQuality.High;
            _settings.Save();
            return MenuCommand.GraphicsChanged;
        }

        if (!confirm) return MenuCommand.None;
        switch (_sel)
        {
            case 0: Open(MenuPage.Video); break;
            case 1: Open(MenuPage.Controls); break;
            case 2:
            case 3: break;
            case 7: Open(MenuPage.Gameplay); break;
            case 8: Open(MenuPage.MusicMixer); break;
            default: Open(_settingsParent); break;
        }
        return MenuCommand.None;
    }

    private MenuCommand UpdateVideo(bool confirm, bool back)
    {
        if (back) { Open(MenuPage.Settings); return MenuCommand.None; }
        if (!confirm) return MenuCommand.None;
        if (_sel < VideoModes.Length)
        {
            _settings.Video = VideoModes[_sel];
            VideoManager.Apply(_settings.Video);
            _settings.Save();
        }
        else
        {
            Open(MenuPage.Settings);
        }
        return MenuCommand.None;
    }

    private MenuCommand UpdateControls(bool confirm, bool back, bool left, bool right)
    {
        if (back) { Open(MenuPage.Settings); return MenuCommand.None; }

        if (_sel < ControlRows)
        {
            if (left) _slot = (_slot + PadSlot) % (PadSlot + 1);
            if (right) _slot = (_slot + 1) % (PadSlot + 1);

            var action = KeyBindings.Actions[_sel];
            if (confirm) _listening = true;
            else if (Raylib.IsKeyPressed(KeyboardKey.Delete) || Raylib.IsKeyPressed(KeyboardKey.Backspace))
            {
                if (_slot == PadSlot) _settings.Keys.ClearPad(action);
                else _settings.Keys.Clear(action, _slot);
                _settings.Save();
            }
        }
        else if (confirm)
        {
            if (_sel == ControlRows)
            {
                _settings.Keys.ResetToDefaults();
                _settings.Save();
            }
            else
            {
                Open(MenuPage.Settings);
            }
        }
        return MenuCommand.None;
    }

    private void UpdateListening()
    {
        if (_slot == PadSlot)
        {
            // Pad slot: only a gamepad button binds; Esc or pad B cancels (Start is reserved).
            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { _listening = false; return; }
            while (Raylib.GetKeyPressed() != 0) { }
            if (Raylib.IsKeyPressed(KeyboardKey.Delete) || Raylib.IsKeyPressed(KeyboardKey.Backspace))
            {
                _settings.Keys.ClearPad(KeyBindings.Actions[_sel]);
                _settings.Save();
                _listening = false;
            }
            else if (PadInput.TryGetPressed(out var button) && _settings.Keys.SetPad(KeyBindings.Actions[_sel], button))
            {
                _settings.Save();
                _listening = false;
            }
            return;
        }

        int pressed;
        while ((pressed = (int)Raylib.GetKeyPressed()) != 0)
        {
            var key = (KeyboardKey)pressed;
            if (key == KeyboardKey.Escape) { _listening = false; return; }

            var action = KeyBindings.Actions[_sel];
            if (key is KeyboardKey.Delete or KeyboardKey.Backspace)
                _settings.Keys.Clear(action, _slot);
            else
                _settings.Keys.Set(action, _slot, key);

            _settings.Save();
            _listening = false;
            return;
        }
    }

    private void OpenSettings(MenuPage parent)
    {
        _settingsParent = parent;
        Open(MenuPage.Settings);
    }

    // ---- Gameplay tuning page ----
    // Row 0: Enemy speed  25..100 % step 5
    // Row 1: Hole open    100..700 ticks step 10
    // Row 2: Refill time  8..60 ticks step 2
    // Row 3: Back

    private static readonly string[] GameplayLabels = ["Enemy speed", "Hole open time", "Refill time", "Back"];

    private string GameplayValue(int row) => row switch
    {
        0 => $"{_settings.EnemySpeedPct}%",
        1 => $"{_settings.HoleOpenTicks} ticks",
        2 => $"{_settings.RefillTicks} ticks",
        _ => "",
    };

    private MenuCommand UpdateGameplay(bool confirm, bool back, bool left, bool right)
    {
        if (back || (confirm && _sel == 3)) { Open(MenuPage.Settings); return MenuCommand.None; }
        if (!left && !right) return MenuCommand.None;

        switch (_sel)
        {
            case 0:
                _settings.EnemySpeedPct = Math.Clamp(_settings.EnemySpeedPct + (right ? 5 : -5), 25, 100);
                break;
            case 1:
                _settings.HoleOpenTicks = Math.Clamp(_settings.HoleOpenTicks + (right ? 10 : -10), 100, 700);
                break;
            case 2:
                _settings.RefillTicks = Math.Clamp(_settings.RefillTicks + (right ? 2 : -2), 8, 60);
                break;
        }
        _settings.Save();
        return MenuCommand.GameplayChanged;
    }

    // ---- Music mixer page ----
    // Row 0: Bass   0..100 step 10
    // Row 1: Pad    0..100 step 10
    // Row 2: Wind   0..100 step 10
    // Row 3: Bells  0..100 step 10
    // Row 4: Back

    private static readonly string[] MixerLabels = ["Bass", "Pad", "Wind", "Bells", "Back"];

    private string MixerValue(int row) => row switch
    {
        0 => $"{_settings.MusicBass}%",
        1 => $"{_settings.MusicPad}%",
        2 => $"{_settings.MusicWind}%",
        3 => $"{_settings.MusicBells}%",
        _ => "",
    };

    private MenuCommand UpdateMusicMixer(bool confirm, bool back, bool left, bool right)
    {
        if (back || (confirm && _sel == 4)) { Open(MenuPage.Settings); return MenuCommand.None; }
        if (!left && !right) return MenuCommand.None;

        int delta = right ? 10 : -10;
        switch (_sel)
        {
            case 0: _settings.MusicBass  = Math.Clamp(_settings.MusicBass  + delta, 0, 100); break;
            case 1: _settings.MusicPad   = Math.Clamp(_settings.MusicPad   + delta, 0, 100); break;
            case 2: _settings.MusicWind  = Math.Clamp(_settings.MusicWind  + delta, 0, 100); break;
            case 3: _settings.MusicBells = Math.Clamp(_settings.MusicBells + delta, 0, 100); break;
        }
        _settings.Save();
        return MenuCommand.MusicMixerChanged;
    }


    private int ItemCount() => Page switch
    {
        MenuPage.Main => MainItems.Length,
        MenuPage.Pause => PauseItems.Length,
        MenuPage.Settings => SettingsItems.Length,
        MenuPage.Video => VideoModes.Length + 1,
        MenuPage.Controls => ControlRows + 2,
        MenuPage.Gameplay => 4,    // 3 sliders + Back
        MenuPage.MusicMixer => 5,  // 4 sliders + Back
        _ => 1,
    };

    private static bool Confirm() =>
        Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) ||
        Raylib.IsKeyPressed(KeyboardKey.Space) || PadInput.ConfirmPressed;

    private static bool Back() => Raylib.IsKeyPressed(KeyboardKey.Escape) || PadInput.BackPressed;

    private static bool Hit(params KeyboardKey[] keys)
    {
        foreach (var k in keys)
            if (Raylib.IsKeyPressed(k) || Raylib.IsKeyPressedRepeat(k)) return true;
        return false;
    }

    // ---- drawing ----

    public void Draw(bool gameBehind)
    {
        if (Page == MenuPage.None) return;

        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();
        float s = sh / 720f;

        Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, gameBehind ? 190 : 215));

        string title = Page switch
        {
            MenuPage.Main => "LODE RUNNER",
            MenuPage.LevelSelect => "SELECT LEVEL",
            MenuPage.HighScores => "HIGH SCORES",
            MenuPage.Pause => "PAUSED",
            MenuPage.Settings => "SETTINGS",
            MenuPage.Video => "VIDEO",
            MenuPage.Gameplay => "GAMEPLAY",
            MenuPage.MusicMixer => "MUSIC MIXER",
            _ => "CONTROLS",
        };
        DrawCentered(title, (int)(sh * 0.14f), (int)(64 * s), Color.Gold, sw);

        switch (Page)
        {
            case MenuPage.Main: DrawList(MainItems, sw, sh, s); break;
            case MenuPage.LevelSelect: DrawLevelSelect(sw, sh, s); break;
            case MenuPage.HighScores: DrawHighScores(sw, sh, s); break;
            case MenuPage.Pause: DrawList(PauseItems, sw, sh, s); break;
            case MenuPage.Settings: DrawList(SettingsItems, sw, sh, s); break;
            case MenuPage.Video: DrawVideo(sw, sh, s); break;
            case MenuPage.Controls: DrawControls(sw, sh, s); break;
            case MenuPage.Gameplay: DrawSliderPage(GameplayLabels, GameplayValue, sw, sh, s); break;
            case MenuPage.MusicMixer: DrawSliderPage(MixerLabels, MixerValue, sw, sh, s); break;
        }

        string hint = Page == MenuPage.HighScores
            ? "Enter / Esc: back"
            : Page == MenuPage.LevelSelect
            ? "Arrows: move     PgUp/PgDn: page     Enter: play     Esc: back"
            : Page == MenuPage.Controls
            ? _listening
                ? (_slot == PadSlot ? "Press a pad button (Start reserved)     Del: clear     Esc: cancel" : "Press a key to bind     Del: clear     Esc: cancel")
                : "Up/Down: row     Left/Right: column     Enter: rebind     Del: clear     Esc: back"
            : Page == MenuPage.Settings
                ? "Up/Down: select     Enter: confirm     Left/Right: change value     Esc: back"
                : Page is MenuPage.Gameplay or MenuPage.MusicMixer
                ? "Up/Down: select     Left/Right: change value     Esc: back"
                : "Up/Down: select     Enter: confirm     Esc: back";
        DrawCentered(hint, sh - (int)(40 * s), (int)(18 * s), Color.Gray, sw);

        if (Page == MenuPage.Main)
        {
            const string credit = "Original game by Douglas Smith (Brøderbund, 1983)";
            int creditSize = Math.Max(8, (int)(13 * s));
            int creditW = UiFont.Measure(credit, creditSize);
            int pad = (int)(18 * s);
            UiFont.Draw(credit, sw - creditW - pad, sh - pad - creditSize, creditSize, new Color(100, 100, 110, 255));
        }
    }

    private void DrawList(string[] items, int sw, int sh, float s)
    {
        int size = (int)(32 * s);
        int step = items.Length > 7 ? (int)(46 * s) : (int)(52 * s);
        int y = items.Length > 7 ? (int)(sh * 0.32f) : (int)(sh * 0.38f);
        for (int i = 0; i < items.Length; i++)
            DrawItem(items[i], i == _sel, y + i * step, size, sw);
    }

    private void DrawLevelSelect(int sw, int sh, float s)
    {
        int cell = (int)(64 * s);
        int gap = (int)(8 * s);
        int size = (int)(24 * s);
        int gridW = LevelCols * cell + (LevelCols - 1) * gap;
        int x0 = (sw - gridW) / 2;
        int y0 = (int)(sh * 0.30f);
        int max = _settings.MaxUnlockedLevel;

        for (int r = 0; r < LevelRowsVisible; r++)
        {
            for (int c = 0; c < LevelCols; c++)
            {
                int idx = (_levelTop + r) * LevelCols + c;
                if (idx >= _levelCount) break;

                int x = x0 + c * (cell + gap);
                int y = y0 + r * (cell + gap);
                bool locked = idx > max;
                bool sel = idx == _levelSel;

                Raylib.DrawRectangle(x, y, cell, cell,
                    sel ? new Color(255, 215, 0, 90) : new Color(255, 255, 255, locked ? 12 : 30));
                if (sel) Raylib.DrawRectangleLines(x, y, cell, cell, Color.Gold);

                string label = (idx + 1).ToString();
                int w = UiFont.Measure(label, size);
                Color col = locked ? new Color(110, 110, 110, 255) : sel ? Color.Gold : Color.RayWhite;
                UiFont.Draw(label, x + (cell - w) / 2, y + (cell - size) / 2, size, col);
            }
        }

        string info = _levelSel <= max
            ? $"Level {_levelSel + 1} of {_levelCount}"
            : $"Level {_levelSel + 1} is locked (unlocked: {max + 1})";
        DrawCentered(info, y0 + LevelRowsVisible * (cell + gap) + (int)(14 * s), (int)(22 * s),
            _levelSel <= max ? Color.RayWhite : Color.Red, sw);
    }

    private void DrawHighScores(int sw, int sh, float s)
    {
        int size = (int)(24 * s), rowH = (int)(40 * s);
        int top = (int)(sh * 0.27f);
        int x0 = sw / 2 - (int)(420 * s);
        int[] cols = [0, (int)(70 * s), (int)(300 * s), (int)(520 * s), (int)(700 * s)];
        var head = new Color(150, 160, 190, 255);
        UiFont.Draw("#", x0 + cols[0], top, size, head);
        UiFont.Draw("SCORE", x0 + cols[1], top, size, head);
        UiFont.Draw("LEVEL", x0 + cols[2], top, size, head);
        UiFont.Draw("DEATHS", x0 + cols[3], top, size, head);
        UiFont.Draw("DATE", x0 + cols[4], top, size, head);

        if (_scores.Entries.Count == 0)
        {
            DrawCentered("No runs yet - go and play!", top + rowH * 3, size, Color.LightGray, sw);
        }
        for (int i = 0; i < _scores.Entries.Count; i++)
        {
            var e = _scores.Entries[i];
            int y = top + (i + 1) * rowH;
            bool hl = i == HighlightRank;
            var col = hl ? Color.Gold : e.Cleared ? Color.SkyBlue : Color.RayWhite;
            if (hl) Raylib.DrawRectangle(x0 - (int)(12 * s), y - (int)(6 * s), (int)(880 * s), rowH - (int)(4 * s), new Color(255, 215, 0, 40));
            UiFont.Draw($"{i + 1}", x0 + cols[0], y, size, col);
            UiFont.Draw($"{e.Score:D7}", x0 + cols[1], y, size, col);
            UiFont.Draw(e.Cleared ? "ALL" : e.Level.ToString(), x0 + cols[2], y, size, col);
            UiFont.Draw(e.Deaths.ToString(), x0 + cols[3], y, size, col);
            UiFont.Draw(e.Date.ToString("yyyy-MM-dd"), x0 + cols[4], y, size, col);
        }

        int fy = top + (HighScoreTable.Capacity + 1) * rowH + (int)(8 * s);
        DrawCentered($"Games: {_scores.GamesPlayed}   Cleared: {_scores.GamesCleared}   Deaths: {_scores.TotalDeaths}   Best level: {_scores.BestLevel}",
            fy, (int)(18 * s), Color.Gray, sw);
    }

    private void DrawVideo(int sw, int sh, float s)
    {
        int size = (int)(32 * s);
        int y = (int)(sh * 0.38f);
        for (int i = 0; i < VideoModes.Length; i++)
        {
            string mark = VideoModes[i] == _settings.Video ? "[x] " : "[ ] ";
            DrawItem(mark + VideoManager.Label(VideoModes[i]), i == _sel, y + i * (int)(52 * s), size, sw);
        }
        DrawItem("Back", _sel == VideoModes.Length, y + VideoModes.Length * (int)(52 * s), size, sw);
    }

    /// <summary>
    /// Generic two-column slider page: left column = label, right column = value.
    /// The last item is always "Back" with no value.
    /// </summary>
    private void DrawSliderPage(string[] labels, Func<int, string> value, int sw, int sh, float s)
    {
        int size  = (int)(32 * s);
        int step  = (int)(54 * s);
        int count = labels.Length;
        int y0    = sh / 2 - step * (count - 1) / 2;
        int labelX = sw / 2 - (int)(280 * s);
        int valueX = sw / 2 + (int)(100 * s);
        for (int i = 0; i < count; i++)
        {
            int y = y0 + i * step;
            bool sel = i == _sel;
            Color col = sel ? Color.Gold : Color.RayWhite;
            UiFont.Draw(labels[i], labelX, y, size, col);
            string v = value(i);
            if (v.Length > 0)
            {
                if (sel)
                {
                    UiFont.Draw("<", valueX - (int)(28 * s), y, size, Color.Gold);
                    UiFont.Draw(">", valueX + UiFont.Measure(v, size) + (int)(8 * s), y, size, Color.Gold);
                }
                UiFont.Draw(v, valueX, y, size, col);
            }
        }
    }

    private void DrawControls(int sw, int sh, float s)
    {
        int size = (int)(26 * s);
        int rowH = (int)(46 * s);
        int cell = (int)(180 * s);
        int top  = (int)(sh * 0.30f);
        int nameX = sw / 2 - (int)(500 * s);
        int slotX = sw / 2 - (int)(310 * s);

        for (int i = 0; i < ControlRows; i++)
        {
            var action = KeyBindings.Actions[i];
            int y = top + i * rowH;
            bool rowSel = i == _sel;
            UiFont.Draw(ActionLabel(action), nameX, y, size, rowSel ? Color.Gold : Color.RayWhite);

            for (int slot = 0; slot <= PadSlot; slot++)
            {
                int x = slotX + slot * cell;
                bool cellSel = rowSel && slot == _slot;
                string text = cellSel && _listening
                    ? (slot == PadSlot ? "press pad..." : "press key...")
                    : slot == PadSlot ? PadInput.Label(_settings.Keys.GetPad(action)) : KeyName(_settings.Keys.Get(action, slot));
                if (cellSel)
                    Raylib.DrawRectangle(x - (int)(8 * s), y - (int)(4 * s), cell - (int)(12 * s), rowH - (int)(8 * s),
                        new Color(255, 215, 0, 50));
                UiFont.Draw(text, x, y, size, cellSel ? Color.Gold : Color.LightGray);
            }
        }

        int tail = top + ControlRows * rowH + (int)(16 * s);
        DrawItem("Reset to defaults", _sel == ControlRows, tail, (int)(30 * s), sw);
        DrawItem("Back", _sel == ControlRows + 1, tail + (int)(48 * s), (int)(30 * s), sw);
    }

    private static void DrawItem(string text, bool selected, int y, int size, int sw)
    {
        string label = selected ? $"> {text} <" : text;
        DrawCentered(label, y, size, selected ? Color.Gold : Color.RayWhite, sw);
    }

    private static void DrawCentered(string text, int y, int size, Color color, int sw)
    {
        int width = UiFont.Measure(text, size);
        UiFont.Draw(text, (sw - width) / 2, y, size, color);
    }

    private static string ActionLabel(GameAction a) => a switch
    {
        GameAction.DigLeft => "Dig left",
        GameAction.DigRight => "Dig right",
        _ => a.ToString(),
    };

    internal static string KeyName(KeyboardKey key)
    {
        if (key == KeyboardKey.Null) return "---";
        string name = key.ToString();
        if (name.StartsWith("Kp", StringComparison.Ordinal)) return "Num " + name[2..];
        return Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");
    }
}
