using System;
using LodeRunner.Core;
using LodeRunner.Core.Levels;
using LodeRunner.Core.Simulation;

namespace LodeRunner.Game.Input;

/// <summary>
/// Body heading in the presentation layer. The 2-D simulation only knows Left/Right;
/// Ladder is an extra heading used while facing a ladder in FPS mode.
/// </summary>
public enum FpsHeading
{
    Right,   // +X, sim Facing.Right
    Left,    // -X, sim Facing.Left
    Ladder,  // -Z, facing into the screen toward a ladder
}

/// <summary>
/// Output produced by <see cref="FpsController.Update"/>.
/// All the information the caller needs to tick the sim and place the camera.
/// </summary>
public readonly struct FpsOutput
{
    /// <summary>Movement + dig input to pass to the simulation tick.</summary>
    public InputState Movement { get; init; }
    /// <summary>One-shot dig left latch (ORed onto the outer latch before the tick).</summary>
    public bool DigLeft { get; init; }
    /// <summary>One-shot dig right latch (ORed onto the outer latch before the tick).</summary>
    public bool DigRight { get; init; }
    /// <summary>Camera yaw in radians. 0 = +X, positive = counter-clockwise viewed from above.</summary>
    public float CameraYaw { get; init; }
    /// <summary>Camera pitch in radians. Positive = look up.</summary>
    public float CameraPitch { get; init; }
    /// <summary>True while a turn is in progress (all movement/dig input is zeroed).</summary>
    public bool Turning { get; init; }
}

/// <summary>
/// FPS mode input controller. Pure logic — no Raylib types, fully testable.
///
/// Coordinate conventions (match IntroCamera / ClassicCamera):
///   Body heading Right  → sim Facing.Right  → yaw 0 (looking along +X)
///   Body heading Left   → sim Facing.Left   → yaw π
///   Body heading Ladder → facing into screen → yaw π/2 (looking along −Z)
///
/// Mouse offsets are clamped to ±π/2 around the body yaw so the player
/// cannot freely orbit behind their back.
/// </summary>
public sealed class FpsController
{
    // ── Turn durations ──────────────────────────────────────────────────
    public const float Turn180Duration = 0.22f;
    public const float Turn90Duration  = 0.12f;

    // ── Mouse sensitivity ───────────────────────────────────────────────
    public const float DefaultMouseSensitivity = 0.0025f;

    // ── Pitch clamp ──────────────────────────────────────────────────────
    private const float PitchMax = 1.309f; // 75°

    // ── State ────────────────────────────────────────────────────────────
    private FpsHeading _body    = FpsHeading.Right;
    private bool       _turning = false;
    private float      _turnProgress = 0f;         // [0, 1]
    private float      _turnDuration = Turn180Duration;
    private float      _turnFromYaw  = 0f;
    private float      _turnToYaw    = 0f;

    // Mouse offsets relative to body yaw (clamped to ±π/2)
    private float _mouseYaw   = 0f;
    private float _mousePitch = 0f;

    public float MouseSensitivity { get; set; } = DefaultMouseSensitivity;

    /// <summary>Current body heading (presentation layer, not the sim's Facing).</summary>
    public FpsHeading Heading => _body;

    /// <summary>True while a turn animation is running.</summary>
    public bool IsTurning => _turning;

    // ────────────────────────────────────────────────────────────────────
    // Public interface
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Process one frame. Must be called every rendered frame while FPS mode is active.
    /// </summary>
    /// <param name="dt">Frame time in seconds.</param>
    /// <param name="mouseDeltaX">Raw mouse delta X (pixels or normalised units from Raylib).</param>
    /// <param name="mouseDeltaY">Raw mouse delta Y (positive = mouse moved down).</param>
    /// <param name="wDown">W key held.</param>
    /// <param name="sDown">S key held.</param>
    /// <param name="aPressed">A key pressed this frame (edge, not held).</param>
    /// <param name="dPressed">D key pressed this frame (edge, not held).</param>
    /// <param name="spacePressed">Space pressed this frame — drops from rope.</param>
    /// <param name="lmbPressed">Left mouse button pressed this frame.</param>
    /// <param name="playerFacing">Current sim Facing (from Player.Facing).</param>
    /// <param name="playerFalling">True while the player is in free fall.</param>
    /// <param name="playerPose">Current sim Pose.</param>
    /// <param name="playerX">Hero's raw X position in sim units (Player.X, not grid col).</param>
    /// <param name="playerGridCol">Hero's current grid column (Player.X / TileUnits, rounded).</param>
    /// <param name="playerGridRow">Hero's current grid row (Player.Y / TileUnits).</param>
    /// <param name="grid">Live grid for tile queries.</param>
    public FpsOutput Update(
        float    dt,
        float    mouseDeltaX,
        float    mouseDeltaY,
        bool     wDown,
        bool     sDown,
        bool     aPressed,
        bool     dPressed,
        bool     spacePressed,
        bool     lmbPressed,
        Facing   playerFacing,
        bool     playerFalling,
        Pose     playerPose,
        int      playerX,
        int      playerGridCol,
        int      playerGridRow,
        Grid     grid)
    {
        // ── 1. Accumulate mouse look ────────────────────────────────────────
        _mouseYaw   -= mouseDeltaX * MouseSensitivity;
        _mousePitch -= mouseDeltaY * MouseSensitivity;

        // During a turn, blend mouse offset toward zero proportionally
        if (_turning)
        {
            float fade = 1f - _turnProgress;
            _mouseYaw   *= fade;
            _mousePitch *= MathF.Min(1f, fade + 0.5f); // pitch fades more gently
        }

        _mousePitch = Math.Clamp(_mousePitch, -PitchMax, PitchMax);

        // ── 2. Advance turn ─────────────────────────────────────────────────
        if (_turning)
        {
            _turnProgress += dt / _turnDuration;
            if (_turnProgress >= 1f)
            {
                _turnProgress = 0f;
                _turning      = false;
                _body         = YawToHeading(_turnToYaw);
                _mouseYaw     = 0f; // reset offset after turn settles
            }
        }

        // ── 3. Determine ladder context ──────────────────────────────────────
        bool onLadder     = grid.Tile(playerGridCol, playerGridRow) == TileType.Ladder;
        bool belowIsLadder = grid.Tile(playerGridCol, playerGridRow + 1) == TileType.Ladder
                             && !grid.IsSolid(playerGridCol, playerGridRow + 1);
        bool ladderAdjRight = IsLadderAt(grid, playerGridCol + 1, playerGridRow);
        bool ladderAdjLeft  = IsLadderAt(grid, playerGridCol - 1, playerGridRow);

        // ── 4. Process turn requests when not already turning ────────────────
        if (!_turning && !playerFalling)
        {
            if (aPressed) StartTurnLeft(onLadder, belowIsLadder, ladderAdjRight, ladderAdjLeft,
                                        playerX, playerGridCol, grid.TileUnits);
            if (dPressed) StartTurnRight(onLadder, belowIsLadder, ladderAdjRight, ladderAdjLeft,
                                         playerX, playerGridCol, grid.TileUnits);

            // S while standing directly above a ladder top (not on a rope, not already on ladder):
            // turn to face it and step down. Only when S is NOT used as step-back below.
            bool onRope  = playerPose == Pose.Hang;
            bool onGround = playerPose is Pose.Stand or Pose.Run;
            bool canStepBack = (onGround || onRope) && _body != FpsHeading.Ladder;
            if (sDown && !canStepBack && !onLadder && belowIsLadder && _body != FpsHeading.Ladder)
                StartTurnToLadder();
        }

        // ── 5. Clamp mouse yaw to hemisphere around body ─────────────────────
        _mouseYaw = Math.Clamp(_mouseYaw, -MathF.PI / 2f, MathF.PI / 2f);

        // ── 6. Compute camera orientation ───────────────────────────────────
        float bodyYaw = _turning
            ? LerpAngle(_turnFromYaw, _turnToYaw, Smoothstep(_turnProgress))
            : HeadingToYaw(_body);
        float camYaw   = bodyYaw + _mouseYaw;
        float camPitch = _mousePitch;

        // ── 7. Translate WASD + Space + LMB into InputState ─────────────────
        InputState movement = default;
        bool digLeft  = false;
        bool digRight = false;

        if (!_turning && !playerFalling)
        {
            bool onRope   = playerPose == Pose.Hang;
            bool onGround = playerPose is Pose.Stand or Pose.Run;

            if (_body == FpsHeading.Ladder)
            {
                // Facing the ladder: W = climb up, S = climb down.
                // Space on a ladder has no special meaning (can't drop).
                movement = new InputState(Up: wDown, Down: sDown);
            }
            else
            {
                // Forward direction along body heading.
                bool fwdRight = _body == FpsHeading.Right;

                // W = move forward.
                bool moveRight = fwdRight  && wDown;
                bool moveLeft  = !fwdRight && wDown;

                // S = step backward (no turn) on solid ground or rope.
                // On a rope S also acts as step-back (staying on the rope, moving the other way).
                if (sDown && (onGround || onRope))
                {
                    moveRight |= !fwdRight; // backward = opposite of forward
                    moveLeft  |= fwdRight;
                }

                // Space on rope = drop (send Down so Locomotion releases the rope into free fall).
                bool dropDown = spacePressed && onRope;

                movement = new InputState(Right: moveRight, Left: moveLeft, Down: dropDown);

                // LMB dig (only when not on a rope — can't dig while hanging).
                if (lmbPressed && !onRope)
                {
                    if (fwdRight) digRight = true;
                    else          digLeft  = true;
                }
            }
        }

        return new FpsOutput
        {
            Movement  = movement,
            DigLeft   = digLeft,
            DigRight  = digRight,
            CameraYaw = camYaw,
            CameraPitch = camPitch,
            Turning   = _turning,
        };
    }

    /// <summary>
    /// Resets the controller to match the sim's facing when FPS mode is first activated
    /// or after a level transition.
    /// </summary>
    public void Reset(Facing facing)
    {
        _body          = facing == Facing.Right ? FpsHeading.Right : FpsHeading.Left;
        _turning       = false;
        _turnProgress  = 0f;
        _mouseYaw      = 0f;
        _mousePitch    = 0f;
    }

    // ────────────────────────────────────────────────────────────────────
    // Turn logic
    // ────────────────────────────────────────────────────────────────────

    private void StartTurnLeft(bool onLadder, bool belowIsLadder, bool ladderAdjRight, bool ladderAdjLeft,
                                int playerX, int playerGridCol, int tileUnits)
    {
        if (_body == FpsHeading.Ladder)
        {
            BeginTurn(HeadingToYaw(FpsHeading.Ladder), HeadingToYaw(FpsHeading.Left), Turn90Duration);
            return;
        }

        if (_body == FpsHeading.Right)
        {
            // Ladder to the left of the player's column (adjLeft) or at current column (on/below).
            // Only turn toward it when the player is within half a tile of the ladder centre.
            int ladderCol = onLadder || belowIsLadder ? playerGridCol : playerGridCol - 1;
            if ((onLadder || belowIsLadder || ladderAdjLeft)
                && IsCloseEnoughToLadder(playerX, ladderCol, tileUnits))
            {
                BeginTurn(HeadingToYaw(FpsHeading.Right), HeadingToYaw(FpsHeading.Ladder), Turn90Duration);
            }
            else
            {
                BeginTurn(HeadingToYaw(FpsHeading.Right), HeadingToYaw(FpsHeading.Left), Turn180Duration);
            }
            return;
        }

        if (_body == FpsHeading.Left)
        {
            int ladderCol = onLadder || belowIsLadder ? playerGridCol : playerGridCol + 1;
            if ((onLadder || belowIsLadder || ladderAdjRight)
                && IsCloseEnoughToLadder(playerX, ladderCol, tileUnits))
            {
                BeginTurn(HeadingToYaw(FpsHeading.Left), HeadingToYaw(FpsHeading.Ladder), Turn90Duration);
            }
            else
            {
                BeginTurn(HeadingToYaw(FpsHeading.Left), HeadingToYaw(FpsHeading.Right), Turn180Duration);
            }
        }
    }

    private void StartTurnRight(bool onLadder, bool belowIsLadder, bool ladderAdjRight, bool ladderAdjLeft,
                                 int playerX, int playerGridCol, int tileUnits)
    {
        if (_body == FpsHeading.Ladder)
        {
            BeginTurn(HeadingToYaw(FpsHeading.Ladder), HeadingToYaw(FpsHeading.Right), Turn90Duration);
            return;
        }

        if (_body == FpsHeading.Right)
        {
            int ladderCol = onLadder || belowIsLadder ? playerGridCol : playerGridCol + 1;
            if ((onLadder || belowIsLadder || ladderAdjRight)
                && IsCloseEnoughToLadder(playerX, ladderCol, tileUnits))
            {
                BeginTurn(HeadingToYaw(FpsHeading.Right), HeadingToYaw(FpsHeading.Ladder), Turn90Duration);
            }
            else
            {
                BeginTurn(HeadingToYaw(FpsHeading.Right), HeadingToYaw(FpsHeading.Left), Turn180Duration);
            }
            return;
        }

        if (_body == FpsHeading.Left)
        {
            int ladderCol = onLadder || belowIsLadder ? playerGridCol : playerGridCol - 1;
            if ((onLadder || belowIsLadder || ladderAdjLeft)
                && IsCloseEnoughToLadder(playerX, ladderCol, tileUnits))
            {
                BeginTurn(HeadingToYaw(FpsHeading.Left), HeadingToYaw(FpsHeading.Ladder), Turn90Duration);
            }
            else
            {
                BeginTurn(HeadingToYaw(FpsHeading.Left), HeadingToYaw(FpsHeading.Right), Turn180Duration);
            }
        }
    }

    private void StartTurnToLadder()
    {
        float from = HeadingToYaw(_body);
        BeginTurn(from, HeadingToYaw(FpsHeading.Ladder), Turn90Duration);
    }

    private void BeginTurn(float fromYaw, float toYaw, float duration)
    {
        _turning       = true;
        _turnProgress  = 0f;
        _turnDuration  = duration;
        _turnFromYaw   = fromYaw;
        _turnToYaw     = toYaw;
    }

    // ────────────────────────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the player is within half a tile of the ladder column centre.
    /// <paramref name="playerX"/> is the raw sim X (Player.X), <paramref name="ladderCol"/> is the
    /// grid column of the ladder, <paramref name="tileUnits"/> is Grid.TileUnits.
    /// </summary>
    private static bool IsCloseEnoughToLadder(int playerX, int ladderCol, int tileUnits)
    {
        int ladderCenterX = ladderCol * tileUnits + tileUnits / 2;
        return Math.Abs(playerX - ladderCenterX) <= tileUnits / 2;
    }

    private static bool IsLadderAt(Grid grid, int col, int row)
    {
        if (!grid.InBounds(col, row)) return false;
        return grid.Tile(col, row) == TileType.Ladder;
    }

    // ────────────────────────────────────────────────────────────────────
    // Math helpers
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Yaw conventions (match IntroCamera):
    ///   yaw 0      → looking along +X (Facing.Right)
    ///   yaw π      → looking along -X (Facing.Left)
    ///   yaw π/2    → looking along -Z (Ladder, into the screen)
    /// </summary>
    public static float HeadingToYaw(FpsHeading h) => h switch
    {
        FpsHeading.Right  => 0f,
        FpsHeading.Ladder => MathF.PI / 2f,
        FpsHeading.Left   => MathF.PI,
        _                 => 0f,
    };

    /// <summary>Not used for <see cref="HeadingToYaw"/> overload above; kept for the call site that passes a float.</summary>
    private static float HeadingToYaw(float alreadyYaw) => alreadyYaw;

    private static FpsHeading YawToHeading(float yaw)
    {
        // Normalise to [0, 2π)
        yaw = ((yaw % (MathF.PI * 2f)) + MathF.PI * 2f) % (MathF.PI * 2f);
        if (yaw < MathF.PI / 4f || yaw >= 7f * MathF.PI / 4f)   return FpsHeading.Right;
        if (yaw < 3f * MathF.PI / 4f)                             return FpsHeading.Ladder;
        if (yaw < 5f * MathF.PI / 4f)                             return FpsHeading.Left;
        return FpsHeading.Ladder; // 5π/4 to 7π/4 → back-hemisphere; shouldn't reach here
    }

    private static float LerpAngle(float from, float to, float t) => from + (to - from) * t;

    private static float Smoothstep(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
