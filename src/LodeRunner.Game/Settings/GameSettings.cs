using System.Text.Json;
using LodeRunner.Core;
using Raylib_cs;

namespace LodeRunner.Game.Settings;

public enum VideoMode
{
    Fullscreen,
    Borderless,
    Windowed,
}

/// <summary>Simulation pace; scales the fixed tick rate without touching the deterministic core.</summary>
public enum GameSpeed
{
    VerySlow,
    Slow,
    Normal,
    Fast,
    VeryFast,
}

public static class GameSpeedInfo
{
    public static double Multiplier(GameSpeed s) => s switch
    {
        GameSpeed.VerySlow => 0.55,
        GameSpeed.Slow => 0.70,
        GameSpeed.Normal => 0.90,
        GameSpeed.Fast => 1.13,
        _ => 1.37,
    };

    public static string Label(GameSpeed s) => s switch
    {
        GameSpeed.VerySlow => "Very slow",
        GameSpeed.Slow => "Slow",
        GameSpeed.Normal => "Normal",
        GameSpeed.Fast => "Fast",
        _ => "Very fast",
    };
}

/// <summary>Visual quality. Low keeps flat lighting only; High adds shadows, bump mapping and gold light.</summary>
public enum GraphicsQuality
{
    Low,
    High,
}

public enum GameAction
{
    Left,
    Right,
    Up,
    Down,
    DigLeft,
    DigRight,
}

/// <summary>
/// Each action has several keyboard slots plus one gamepad button; any of them triggers it.
/// Direction actions also react to the left stick.
/// </summary>
public sealed class KeyBindings
{
    public const int Slots = 3;
    public static readonly GameAction[] Actions = Enum.GetValues<GameAction>();

    private readonly KeyboardKey[,] _keys = new KeyboardKey[Actions.Length, Slots];
    private readonly GamepadButton[] _pad = new GamepadButton[Actions.Length];

    public static KeyBindings CreateDefault()
    {
        var b = new KeyBindings();
        b.Fill(GameAction.Left, KeyboardKey.Left, KeyboardKey.A, KeyboardKey.Kp4);
        b.Fill(GameAction.Right, KeyboardKey.Right, KeyboardKey.D, KeyboardKey.Kp6);
        b.Fill(GameAction.Up, KeyboardKey.Up, KeyboardKey.W, KeyboardKey.Kp8);
        b.Fill(GameAction.Down, KeyboardKey.Down, KeyboardKey.S, KeyboardKey.Kp2);
        b.Fill(GameAction.DigLeft, KeyboardKey.Z, KeyboardKey.Null, KeyboardKey.Null);
        b.Fill(GameAction.DigRight, KeyboardKey.X, KeyboardKey.Null, KeyboardKey.Null);
        b._pad[(int)GameAction.Left] = GamepadButton.LeftFaceLeft;
        b._pad[(int)GameAction.Right] = GamepadButton.LeftFaceRight;
        b._pad[(int)GameAction.Up] = GamepadButton.LeftFaceUp;
        b._pad[(int)GameAction.Down] = GamepadButton.LeftFaceDown;
        b._pad[(int)GameAction.DigLeft] = GamepadButton.RightFaceLeft;
        b._pad[(int)GameAction.DigRight] = GamepadButton.RightFaceRight;
        return b;
    }

    private void Fill(GameAction a, params KeyboardKey[] keys)
    {
        for (int s = 0; s < Slots; s++) _keys[(int)a, s] = s < keys.Length ? keys[s] : KeyboardKey.Null;
    }

    public KeyboardKey Get(GameAction a, int slot) => _keys[(int)a, slot];

    /// <summary>Binds a key; the same key is removed from every other slot so a key never has two meanings.</summary>
    public void Set(GameAction a, int slot, KeyboardKey key)
    {
        if (key == KeyboardKey.Null) { Clear(a, slot); return; }
        foreach (var other in Actions)
            for (int s = 0; s < Slots; s++)
                if (_keys[(int)other, s] == key) _keys[(int)other, s] = KeyboardKey.Null;
        _keys[(int)a, slot] = key;
    }

    public void Clear(GameAction a, int slot) => _keys[(int)a, slot] = KeyboardKey.Null;

    public GamepadButton GetPad(GameAction a) => _pad[(int)a];

    /// <summary>Binds a pad button; it is removed from every other action. Start is reserved and refused.</summary>
    public bool SetPad(GameAction a, GamepadButton button)
    {
        if (button == GamepadButton.Unknown) { ClearPad(a); return true; }
        if (PadInput.IsReserved(button)) return false;
        for (int i = 0; i < _pad.Length; i++)
            if (_pad[i] == button) _pad[i] = GamepadButton.Unknown;
        _pad[(int)a] = button;
        return true;
    }

    public void ClearPad(GameAction a) => _pad[(int)a] = GamepadButton.Unknown;

    private static int StickDir(GameAction a) => a switch
    {
        GameAction.Up => PadInput.DirUp,
        GameAction.Down => PadInput.DirDown,
        GameAction.Left => PadInput.DirLeft,
        GameAction.Right => PadInput.DirRight,
        _ => -1,
    };

    public void ResetToDefaults()
    {
        var d = CreateDefault();
        Array.Copy(d._keys, _keys, _keys.Length);
        Array.Copy(d._pad, _pad, _pad.Length);
    }

    public bool IsBound(KeyboardKey key)
    {
        if (key == KeyboardKey.Null) return false;
        foreach (var a in Actions)
            for (int s = 0; s < Slots; s++)
                if (_keys[(int)a, s] == key) return true;
        return false;
    }

    public bool IsDown(GameAction a)
    {
        for (int s = 0; s < Slots; s++)
        {
            var k = _keys[(int)a, s];
            if (k != KeyboardKey.Null && Raylib.IsKeyDown(k)) return true;
        }
        var pad = _pad[(int)a];
        if (pad != GamepadButton.Unknown && PadInput.Down(pad)) return true;
        int dir = StickDir(a);
        return dir >= 0 && PadInput.StickDown(dir);
    }

    public bool WasPressed(GameAction a)
    {
        for (int s = 0; s < Slots; s++)
        {
            var k = _keys[(int)a, s];
            if (k != KeyboardKey.Null && Raylib.IsKeyPressed(k)) return true;
        }
        var pad = _pad[(int)a];
        return pad != GamepadButton.Unknown && PadInput.Pressed(pad);
    }

    public Dictionary<string, string[]> ToDto()
    {
        var d = new Dictionary<string, string[]>();
        foreach (var a in Actions)
        {
            var arr = new string[Slots];
            for (int s = 0; s < Slots; s++) arr[s] = _keys[(int)a, s].ToString();
            d[a.ToString()] = arr;
        }
        return d;
    }

    public Dictionary<string, string> ToPadDto()
    {
        var d = new Dictionary<string, string>();
        foreach (var a in Actions) d[a.ToString()] = _pad[(int)a].ToString();
        return d;
    }

    public void LoadPadDto(Dictionary<string, string>? dto)
    {
        if (dto is null) return; // old settings file: keep defaults
        foreach (var a in Actions)
        {
            if (!dto.TryGetValue(a.ToString(), out var name)) continue;
            _pad[(int)a] = Enum.TryParse<GamepadButton>(name, out var b) && !PadInput.IsReserved(b)
                ? b
                : GamepadButton.Unknown;
        }
    }

    public void LoadDto(Dictionary<string, string[]>? dto)
    {
        if (dto is null) return;
        foreach (var a in Actions)
        {
            if (!dto.TryGetValue(a.ToString(), out var arr)) continue;
            for (int s = 0; s < Slots; s++)
            {
                var k = KeyboardKey.Null;
                if (s < arr.Length) Enum.TryParse(arr[s], out k);
                _keys[(int)a, s] = k;
            }
        }
    }
}

/// <summary>Persistent user settings (video mode, key bindings), stored as JSON in the user's AppData.</summary>
public sealed class GameSettings
{
    private sealed class FileDto
    {
        public string Video { get; set; } = nameof(VideoMode.Windowed);
        public Dictionary<string, string[]>? Keys { get; set; }
        public Dictionary<string, string>? Pad { get; set; }
        public int MaxUnlockedLevel { get; set; }
        public int Volume { get; set; } = GameSettings.DefaultVolume;
        public int? MusicVolume { get; set; }
        public string EnemyAi { get; set; } = nameof(EnemyAiMode.Classic);
        public string Speed { get; set; } = nameof(GameSpeed.Normal);
        public string Graphics { get; set; } = nameof(GraphicsQuality.High);

        // Gameplay tuning (optional — absent in old saves means "default")
        public int? EnemySpeedPct { get; set; }
        public int? HoleOpenTicks { get; set; }
        public int? RefillTicks { get; set; }

        // Music mixer (optional)
        public int? MusicBass { get; set; }
        public int? MusicPad { get; set; }
        public int? MusicWind { get; set; }
        public int? MusicBells { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public const int DefaultVolume = 70;
    public const int DefaultMusicVolume = 60;

    // Gameplay tuning defaults (match GameConfig defaults)
    public const int DefaultEnemySpeedPct = 75;  // 75% of player speed = EnemyRunSpeed 3 / PlayerRunSpeed 4
    public const int DefaultHoleOpenTicks = 430;
    public const int DefaultRefillTicks   = 24;

    // Music mixer defaults (100 = original generator level)
    public const int DefaultMusicBass  = 100;
    public const int DefaultMusicPad   = 100;
    public const int DefaultMusicWind  = 100;
    public const int DefaultMusicBells = 100;

    public VideoMode Video { get; set; } = VideoMode.Windowed;

    /// <summary>Sound effects volume in percent, 0..100 (multiples of 10 in the menu).</summary>
    public int Volume { get; set; } = DefaultVolume;

    /// <summary>Procedural dark ambient music volume in percent, 0..100 (multiples of 10 in the menu).</summary>
    public int MusicVolume { get; set; } = DefaultMusicVolume;

    /// <summary>Guard behaviour: Classic (original rules) or Smart (shortest-path search).</summary>
    public EnemyAiMode EnemyAi { get; set; } = EnemyAiMode.Classic;

    /// <summary>Game pace: scales ticks per second for the player and the guards alike.</summary>
    public GameSpeed Speed { get; set; } = GameSpeed.Normal;

    /// <summary>Rendering quality; Low is for weak graphics cards.</summary>
    public GraphicsQuality Graphics { get; set; } = GraphicsQuality.High;
    public KeyBindings Keys { get; } = KeyBindings.CreateDefault();

    // ---- Gameplay tuning ----

    /// <summary>Enemy run/climb speed as a percentage of player speed (25–100 %).</summary>
    public int EnemySpeedPct { get; set; } = DefaultEnemySpeedPct;

    /// <summary>Ticks the hole stays open before it starts refilling (100–700).</summary>
    public int HoleOpenTicks { get; set; } = DefaultHoleOpenTicks;

    /// <summary>Ticks it takes for a hole to fully refill (8–60).</summary>
    public int RefillTicks { get; set; } = DefaultRefillTicks;

    // ---- Music mixer ----

    /// <summary>Sub-bass drone level 0–100 %.</summary>
    public int MusicBass  { get; set; } = DefaultMusicBass;
    /// <summary>Unison pad level 0–100 %.</summary>
    public int MusicPad   { get; set; } = DefaultMusicPad;
    /// <summary>Cavern wind level 0–100 %.</summary>
    public int MusicWind  { get; set; } = DefaultMusicWind;
    /// <summary>Distant bells level 0–100 %.</summary>
    public int MusicBells { get; set; } = DefaultMusicBells;

    /// <summary>Zero-based index of the furthest level the player may start from (level select / continue).</summary>
    public int MaxUnlockedLevel { get; private set; }

    /// <summary>Builds a GameConfig that reflects the current gameplay tuning settings.</summary>
    public GameConfig ToGameConfig()
    {
        // EnemySpeedPct is relative to PlayerRunSpeed (4). Allowed enemy speeds must divide TileUnits (48).
        // Valid divisors of 48: 1,2,3,4,6,8,12,16,24,48. We clamp to 1–4 (the meaningful range).
        int enemyRun = Math.Clamp((int)MathF.Round(4f * EnemySpeedPct / 100f), 1, 4);
        return new GameConfig
        {
            EnemyRunSpeed   = enemyRun,
            EnemyClimbSpeed = enemyRun,
            HoleOpenTicks   = HoleOpenTicks,
            RefillTicks     = RefillTicks,
        };
    }

    /// <summary>Unlocks up to <paramref name="levelIndex"/>; returns true (and saves) when progress advanced.</summary>
    public bool Unlock(int levelIndex, int levelCount)
    {
        int target = Math.Clamp(levelIndex, 0, Math.Max(0, levelCount - 1));
        if (target <= MaxUnlockedLevel) return false;
        MaxUnlockedLevel = target;
        Save();
        return true;
    }

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LodeRunner", "settings.json");

    public static GameSettings Load()
    {
        var s = new GameSettings();
        try
        {
            if (!File.Exists(FilePath)) return s;
            var dto = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(FilePath), JsonOptions);
            if (dto is null) return s;
            if (Enum.TryParse<VideoMode>(dto.Video, out var v)) s.Video = v;
            s.Keys.LoadDto(dto.Keys);
            s.Keys.LoadPadDto(dto.Pad);
            s.MaxUnlockedLevel = Math.Max(0, dto.MaxUnlockedLevel);
            s.Volume = Math.Clamp(dto.Volume, 0, 100);
            s.MusicVolume = Math.Clamp(dto.MusicVolume ?? DefaultMusicVolume, 0, 100);
            if (Enum.TryParse<EnemyAiMode>(dto.EnemyAi, out var ai)) s.EnemyAi = ai;
            if (Enum.TryParse<GameSpeed>(dto.Speed, out var sp) && Enum.IsDefined(sp)) s.Speed = sp;
            if (Enum.TryParse<GraphicsQuality>(dto.Graphics, out var gq) && Enum.IsDefined(gq)) s.Graphics = gq;
            if (dto.EnemySpeedPct is int esp) s.EnemySpeedPct = Math.Clamp(esp, 25, 100);
            if (dto.HoleOpenTicks is int hot) s.HoleOpenTicks = Math.Clamp(hot, 100, 700);
            if (dto.RefillTicks   is int rft) s.RefillTicks   = Math.Clamp(rft, 8, 60);
            if (dto.MusicBass  is int mb)  s.MusicBass  = Math.Clamp(mb,  0, 100);
            if (dto.MusicPad   is int mp)  s.MusicPad   = Math.Clamp(mp,  0, 100);
            if (dto.MusicWind  is int mw)  s.MusicWind  = Math.Clamp(mw,  0, 100);
            if (dto.MusicBells is int mbl) s.MusicBells = Math.Clamp(mbl, 0, 100);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable file: fall back to defaults.
        }
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var dto = new FileDto
            {
                Video = Video.ToString(), Keys = Keys.ToDto(), Pad = Keys.ToPadDto(),
                MaxUnlockedLevel = MaxUnlockedLevel, Volume = Volume, MusicVolume = MusicVolume,
                EnemyAi = EnemyAi.ToString(), Speed = Speed.ToString(), Graphics = Graphics.ToString(),
                EnemySpeedPct = EnemySpeedPct, HoleOpenTicks = HoleOpenTicks, RefillTicks = RefillTicks,
                MusicBass = MusicBass, MusicPad = MusicPad, MusicWind = MusicWind, MusicBells = MusicBells,
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(dto, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; failing to save must not crash the game.
        }
    }
}
