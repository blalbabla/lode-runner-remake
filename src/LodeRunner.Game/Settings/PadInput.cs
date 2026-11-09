using Raylib_cs;

namespace LodeRunner.Game.Settings;

/// <summary>
/// Gamepad polling over every connected pad. Menus use fixed buttons so a bad rebind can never lock the player out:
/// d-pad / left stick navigate (with repeat), A confirms, B or Start goes back, LB/RB page.
/// Call <see cref="Update"/> once per frame before reading anything else.
/// </summary>
public static class PadInput
{
    public const int DirUp = 0, DirDown = 1, DirLeft = 2, DirRight = 3;

    private const int MaxPads = 4;
    private const float Dead = 0.5f;
    private const float RepeatDelay = 0.4f;
    private const float RepeatRate = 0.11f;

    private static readonly GamepadButton[] AllButtons =
        Enum.GetValues<GamepadButton>().Where(b => b != GamepadButton.Unknown).ToArray();

    private static readonly float[] Held = [-1f, -1f, -1f, -1f];
    private static readonly float[] NextRepeat = new float[4];
    private static readonly bool[] Nav = new bool[4];

    /// <summary>Start is reserved for pause / back and cannot be rebound.</summary>
    public static bool IsReserved(GamepadButton b) => b == GamepadButton.MiddleRight;

    public static bool Connected
    {
        get
        {
            for (int i = 0; i < MaxPads; i++)
                if (Raylib.IsGamepadAvailable(i)) return true;
            return false;
        }
    }

    public static bool NavUp => Nav[DirUp];
    public static bool NavDown => Nav[DirDown];
    public static bool NavLeft => Nav[DirLeft];
    public static bool NavRight => Nav[DirRight];

    public static bool ConfirmPressed => Pressed(GamepadButton.RightFaceDown);
    public static bool BackPressed => Pressed(GamepadButton.RightFaceRight) || StartPressed;
    public static bool StartPressed => Pressed(GamepadButton.MiddleRight);
    public static bool PagePrev => Pressed(GamepadButton.LeftTrigger1);
    public static bool PageNext => Pressed(GamepadButton.RightTrigger1);

    public static bool AnyPressed => TryGetPressed(out _);

    public static void Update(float dt)
    {
        for (int d = 0; d < 4; d++)
        {
            if (!IsDirDown(d))
            {
                Held[d] = -1f;
                Nav[d] = false;
            }
            else if (Held[d] < 0f)
            {
                Held[d] = 0f;
                NextRepeat[d] = RepeatDelay;
                Nav[d] = true;
            }
            else
            {
                Held[d] += dt;
                Nav[d] = Held[d] >= NextRepeat[d];
                if (Nav[d]) NextRepeat[d] += RepeatRate;
            }
        }
    }

    public static bool Pressed(GamepadButton b)
    {
        for (int i = 0; i < MaxPads; i++)
            if (Raylib.IsGamepadAvailable(i) && Raylib.IsGamepadButtonPressed(i, b)) return true;
        return false;
    }

    public static bool Down(GamepadButton b)
    {
        for (int i = 0; i < MaxPads; i++)
            if (Raylib.IsGamepadAvailable(i) && Raylib.IsGamepadButtonDown(i, b)) return true;
        return false;
    }

    /// <summary>First button pressed this frame on any pad (used while rebinding).</summary>
    public static bool TryGetPressed(out GamepadButton button)
    {
        foreach (var b in AllButtons)
        {
            if (Pressed(b))
            {
                button = b;
                return true;
            }
        }
        button = GamepadButton.Unknown;
        return false;
    }

    /// <summary>Left stick pushed in a direction (one of <see cref="Up"/>, <see cref="Down"/>, ...).</summary>
    public static bool StickDown(int dir)
    {
        for (int i = 0; i < MaxPads; i++)
        {
            if (!Raylib.IsGamepadAvailable(i)) continue;
            float v = dir is DirLeft or DirRight
                ? Raylib.GetGamepadAxisMovement(i, GamepadAxis.LeftX)
                : Raylib.GetGamepadAxisMovement(i, GamepadAxis.LeftY);
            if (float.IsNaN(v)) continue;
            if (dir is DirLeft or DirUp ? v < -Dead : v > Dead) return true;
        }
        return false;
    }

    private static bool IsDirDown(int dir)
    {
        var dpad = dir switch
        {
            DirUp => GamepadButton.LeftFaceUp,
            DirDown => GamepadButton.LeftFaceDown,
            DirLeft => GamepadButton.LeftFaceLeft,
            _ => GamepadButton.LeftFaceRight,
        };
        return Down(dpad) || StickDown(dir);
    }

    public static string Label(GamepadButton b) => b switch
    {
        GamepadButton.Unknown => "---",
        GamepadButton.LeftFaceUp => "Pad Up",
        GamepadButton.LeftFaceDown => "Pad Down",
        GamepadButton.LeftFaceLeft => "Pad Left",
        GamepadButton.LeftFaceRight => "Pad Right",
        GamepadButton.RightFaceUp => "Pad Y",
        GamepadButton.RightFaceDown => "Pad A",
        GamepadButton.RightFaceLeft => "Pad X",
        GamepadButton.RightFaceRight => "Pad B",
        GamepadButton.LeftTrigger1 => "Pad LB",
        GamepadButton.LeftTrigger2 => "Pad LT",
        GamepadButton.RightTrigger1 => "Pad RB",
        GamepadButton.RightTrigger2 => "Pad RT",
        GamepadButton.MiddleLeft => "Pad Back",
        GamepadButton.Middle => "Pad Guide",
        GamepadButton.MiddleRight => "Pad Start",
        GamepadButton.LeftThumb => "Pad LS",
        GamepadButton.RightThumb => "Pad RS",
        _ => b.ToString(),
    };
}
