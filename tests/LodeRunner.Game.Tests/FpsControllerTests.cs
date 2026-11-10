using LodeRunner.Core;
using LodeRunner.Core.Levels;
using LodeRunner.Core.Simulation;
using LodeRunner.Game.Input;

namespace LodeRunner.Game.Tests;

/// <summary>
/// Unit tests for <see cref="FpsController"/>. No Raylib window is opened; Grid is built
/// directly from LevelParser so the test can probe ladder context.
/// </summary>
public class FpsControllerTests
{
    private static readonly GameConfig Cfg = new();

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Builds a 28×16 Grid from bottom rows (the same convention as TestWorld).</summary>
    private static Grid MakeGrid(params string[] bottomRows)
    {
        var all = new List<string>();
        for (int i = 0; i < LevelParser.Rows - bottomRows.Length; i++)
        {
            // First padding row gets gold ($) + player start (&) so the level is valid.
            all.Add(i == 0 ? "&" + new string(' ', 26) + "$" : "");
        }
        all.AddRange(bottomRows);
        var level = LevelParser.Parse(string.Join("\n", all));
        return new Grid(level, Cfg.TileUnits);
    }

    private static FpsOutput Tick(
        FpsController ctrl,
        Grid grid,
        int col = 0, int row = 14,
        Facing facing = Facing.Right,
        bool falling = false,
        Pose pose = Pose.Stand,
        bool w = false, bool s = false,
        bool a = false, bool d = false,
        bool lmb = false,
        float dt = 0.016f,
        float mx = 0f, float my = 0f) =>
        ctrl.Update(dt, mx, my, w, s, a, d, lmb, facing, falling, pose, col, row, grid);

    // ── Heading constants ─────────────────────────────────────────────────

    [Fact]
    public void HeadingToYaw_Right_IsZero()
    {
        Assert.Equal(0f, FpsController.HeadingToYaw(FpsHeading.Right), 4);
    }

    [Fact]
    public void HeadingToYaw_Left_IsPi()
    {
        Assert.Equal(MathF.PI, FpsController.HeadingToYaw(FpsHeading.Left), 4);
    }

    [Fact]
    public void HeadingToYaw_Ladder_IsHalfPi()
    {
        Assert.Equal(MathF.PI / 2f, FpsController.HeadingToYaw(FpsHeading.Ladder), 4);
    }

    // ── W key produces movement ───────────────────────────────────────────

    [Fact]
    public void W_FacingRight_ProducesRight()
    {
        var ctrl = new FpsController();
        ctrl.Reset(Facing.Right);
        var grid = MakeGrid("############################", "                            ");
        var out1 = Tick(ctrl, grid, col: 1, row: 14, facing: Facing.Right, pose: Pose.Run, w: true);
        Assert.True(out1.Movement.Right);
        Assert.False(out1.Movement.Left);
    }

    [Fact]
    public void W_FacingLeft_ProducesLeft()
    {
        var ctrl = new FpsController();
        ctrl.Reset(Facing.Left);
        var grid = MakeGrid("############################", "                            ");
        var out1 = Tick(ctrl, grid, col: 1, row: 14, facing: Facing.Left, pose: Pose.Run, w: true);
        Assert.True(out1.Movement.Left);
        Assert.False(out1.Movement.Right);
    }

    // ── A/D turn requests ─────────────────────────────────────────────────

    [Fact]
    public void A_FromRight_StartsTurn()
    {
        var ctrl = new FpsController();
        ctrl.Reset(Facing.Right);
        var grid = MakeGrid("############################", "                            ");
        var out1 = Tick(ctrl, grid, col: 1, row: 14, facing: Facing.Right, pose: Pose.Stand, a: true);
        Assert.True(out1.Turning);
        // During turn, all movement is zeroed
        Assert.False(out1.Movement.Right);
        Assert.False(out1.Movement.Left);
    }

    [Fact]
    public void D_FromLeft_StartsTurn()
    {
        var ctrl = new FpsController();
        ctrl.Reset(Facing.Left);
        var grid = MakeGrid("############################", "                            ");
        var out1 = Tick(ctrl, grid, col: 1, row: 14, facing: Facing.Left, pose: Pose.Stand, d: true);
        Assert.True(out1.Turning);
    }

    [Fact]
    public void Turn180_CompletesAfterDuration()
    {
        var ctrl = new FpsController();
        ctrl.Reset(Facing.Right);
        var grid = MakeGrid("############################", "                            ");
        // Kick off the turn
        Tick(ctrl, grid, col: 1, row: 14, facing: Facing.Right, pose: Pose.Stand, a: true);
        Assert.Equal(FpsHeading.Right, ctrl.Heading); // still at start
        // Advance well past the turn duration
        for (int i = 0; i < 20; i++)
            Tick(ctrl, grid, col: 1, row: 14, facing: Facing.Right, pose: Pose.Stand, dt: 0.02f);
        Assert.False(ctrl.IsTurning);
        Assert.Equal(FpsHeading.Left, ctrl.Heading);
    }

    [Fact]
    public void Turn90_ToLadder_CompletesAtHalfPiYaw()
    {
        var ctrl = new FpsController();
        ctrl.Reset(Facing.Right);
        // Grid with a ladder at col 1, row 14 (the player's column).
        var grid = MakeGrid(
            "############################",  // row 15
            "                            ",  // row 14
            "   H                        "); // row 13 (not relevant here)
        // Ladder at col 1, row 14 — place it below the player's feet
        var grid2 = MakeGrid(
            "############################",
            " H                          ",
            "                            ");
        // col=1 row=13, ladder at col=1 row=14 (below)
        Tick(ctrl, grid2, col: 1, row: 13, facing: Facing.Right, pose: Pose.Stand, d: true);
        Assert.True(ctrl.IsTurning);
        for (int i = 0; i < 20; i++)
            Tick(ctrl, grid2, col: 1, row: 13, dt: 0.02f);
        Assert.Equal(FpsHeading.Ladder, ctrl.Heading);
    }

    // ── Turning blocks movement ───────────────────────────────────────────

    [Fact]
    public void MovementBlockedDuringTurn()
    {
        var ctrl = new FpsController();
        ctrl.Reset(Facing.Right);
        var grid = MakeGrid("############################", "                            ");
        // Start a 180° turn
        Tick(ctrl, grid, a: true, col: 1, row: 14, facing: Facing.Right);
        // Try to move while turning
        var midOut = Tick(ctrl, grid, w: true, col: 1, row: 14, facing: Facing.Right, dt: 0.05f);
        Assert.True(midOut.Turning);
        Assert.False(midOut.Movement.Right);
        Assert.False(midOut.Movement.Left);
        Assert.False(midOut.Movement.Up);
        Assert.False(midOut.Movement.Down);
    }

    // ── LMB dig ───────────────────────────────────────────────────────────

    [Fact]
    public void LMB_FacingRight_ProducesDigRight()
    {
        var ctrl = new FpsController();
        ctrl.Reset(Facing.Right);
        var grid = MakeGrid("############################", "                            ");
        var out1 = Tick(ctrl, grid, col: 1, row: 14, facing: Facing.Right, pose: Pose.Stand, lmb: true);
        Assert.True(out1.DigRight);
        Assert.False(out1.DigLeft);
    }

    [Fact]
    public void LMB_FacingLeft_ProducesDigLeft()
    {
        var ctrl = new FpsController();
        ctrl.Reset(Facing.Left);
        var grid = MakeGrid("############################", "                            ");
        var out1 = Tick(ctrl, grid, col: 2, row: 14, facing: Facing.Left, pose: Pose.Stand, lmb: true);
        Assert.True(out1.DigLeft);
        Assert.False(out1.DigRight);
    }

    [Fact]
    public void LMB_FacingLadder_IsIgnored()
    {
        var ctrl = new FpsController();
        ctrl.Reset(Facing.Right);
        // Grid with ladder at col=1 row=14 (below player at row 13)
        var grid = MakeGrid(
            "############################",
            " H                          ",
            "                            ");
        // Turn to face the ladder first
        Tick(ctrl, grid, col: 1, row: 13, facing: Facing.Right, pose: Pose.Stand, d: true);
        for (int i = 0; i < 20; i++)
            Tick(ctrl, grid, col: 1, row: 13, dt: 0.02f);
        Assert.Equal(FpsHeading.Ladder, ctrl.Heading);
        // Now LMB should be ignored
        var out1 = Tick(ctrl, grid, col: 1, row: 13, facing: Facing.Right, pose: Pose.Stand, lmb: true);
        Assert.False(out1.DigLeft);
        Assert.False(out1.DigRight);
    }

    // ── Ladder movement ───────────────────────────────────────────────────

    [Fact]
    public void W_FacingLadder_ProducesUp()
    {
        var ctrl = new FpsController();
        ctrl.Reset(Facing.Right);
        var grid = MakeGrid(
            "############################",
            " H                          ",
            "                            ");
        // Turn to ladder
        Tick(ctrl, grid, col: 1, row: 13, facing: Facing.Right, pose: Pose.Stand, d: true);
        for (int i = 0; i < 20; i++)
            Tick(ctrl, grid, col: 1, row: 13, dt: 0.02f);
        var out1 = Tick(ctrl, grid, col: 1, row: 13, pose: Pose.Climb, w: true);
        Assert.True(out1.Movement.Up);
        Assert.False(out1.Movement.Down);
    }

    [Fact]
    public void S_FacingLadder_ProducesDown()
    {
        var ctrl = new FpsController();
        ctrl.Reset(Facing.Right);
        var grid = MakeGrid(
            "############################",
            " H                          ",
            "                            ");
        Tick(ctrl, grid, col: 1, row: 13, facing: Facing.Right, pose: Pose.Stand, d: true);
        for (int i = 0; i < 20; i++)
            Tick(ctrl, grid, col: 1, row: 13, dt: 0.02f);
        var out1 = Tick(ctrl, grid, col: 1, row: 13, pose: Pose.Climb, s: true);
        Assert.True(out1.Movement.Down);
    }

    // ── Mouse yaw clamp ───────────────────────────────────────────────────

    [Fact]
    public void MouseYaw_ClampedToHemisphere()
    {
        var ctrl = new FpsController();
        ctrl.Reset(Facing.Right);
        var grid = MakeGrid("############################", "                            ");
        // Dump a large mouse delta to the right (positive deltaX reduces yaw)
        var out1 = Tick(ctrl, grid, col: 1, row: 14, mx: 10000f);
        // Yaw should not exceed π/2 beyond body heading
        float bodyYaw = FpsController.HeadingToYaw(FpsHeading.Right); // 0
        Assert.True(out1.CameraYaw >= bodyYaw - MathF.PI / 2f - 0.01f,
            $"CameraYaw {out1.CameraYaw} clamped below -π/2 from body");
        Assert.True(out1.CameraYaw <= bodyYaw + MathF.PI / 2f + 0.01f,
            $"CameraYaw {out1.CameraYaw} clamped above +π/2 from body");
    }

    // ── Reset ─────────────────────────────────────────────────────────────

    [Fact]
    public void Reset_SetsHeadingFromFacing()
    {
        var ctrl = new FpsController();
        ctrl.Reset(Facing.Left);
        Assert.Equal(FpsHeading.Left, ctrl.Heading);
        ctrl.Reset(Facing.Right);
        Assert.Equal(FpsHeading.Right, ctrl.Heading);
    }

    [Fact]
    public void Reset_ClearsTurning()
    {
        var ctrl = new FpsController();
        ctrl.Reset(Facing.Right);
        var grid = MakeGrid("############################", "                            ");
        Tick(ctrl, grid, a: true, col: 1, row: 14);
        Assert.True(ctrl.IsTurning);
        ctrl.Reset(Facing.Right);
        Assert.False(ctrl.IsTurning);
    }
}
