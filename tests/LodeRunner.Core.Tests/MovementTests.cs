using LodeRunner.Core.Simulation;
using static LodeRunner.Core.Tests.TestWorld;

namespace LodeRunner.Core.Tests;

public class MovementTests
{
    private static readonly string Floor = new('#', 28);
    private static readonly InputState Right = new(Right: true);
    private static readonly InputState Left = new(Left: true);
    private static readonly InputState Up = new(Up: true);
    private static readonly InputState Down = new(Down: true);
    private static readonly InputState Idle = default;

    [Fact]
    public void PlayerStartsOnItsMarkedCell()
    {
        var w = Create("&", Floor);
        Assert.Equal(0, w.Player.X);
        Assert.Equal(14 * T, w.Player.Y);
        Assert.Equal(Pose.Stand, w.Player.Pose);
    }

    [Fact]
    public void RunsAtConstantSpeed()
    {
        var w = Create("&", Floor);
        Run(w, Right, 12); // 12 ticks * 4 units = one cell
        Assert.Equal(T, w.Player.X);
        Assert.Equal(Pose.Run, w.Player.Pose);
        Assert.Equal(Facing.Right, w.Player.Facing);
    }

    [Fact]
    public void SpeedHasNoAcceleration()
    {
        var w = Create("&", Floor);
        int last = w.Player.X;
        for (int i = 0; i < 20; i++)
        {
            w.Tick(Right);
            Assert.Equal(4, w.Player.X - last);
            last = w.Player.X;
        }
    }

    [Fact]
    public void StopsAtBrickWall()
    {
        var w = Create("& #", Floor);
        Run(w, Right, 100);
        Assert.Equal(T, w.Player.X);
    }

    [Fact]
    public void StopsAtLevelEdge()
    {
        var w = Create("&", Floor);
        Run(w, Left, 50);
        Assert.Equal(0, w.Player.X);
        Facing f = w.Player.Facing;
        Assert.Equal(Facing.Left, f);
    }

    [Fact]
    public void StopsAtRightLevelEdge()
    {
        var w = Create("&", Floor);
        Run(w, Right, 1000);
        Assert.Equal(27 * T, w.Player.X);
    }

    [Fact]
    public void PreviousPositionIsKeptForInterpolation()
    {
        var w = Create("&", Floor);
        w.Tick(Right);
        w.Tick(Right);
        Assert.Equal(4, w.Player.PrevX);
        Assert.Equal(8, w.Player.X);
    }

    [Fact]
    public void FallsAtConstantSpeedAndLandsOnFloor()
    {
        var w = Create("&", "", "", Floor);
        int last = w.Player.Y;
        for (int i = 0; i < 24; i++)
        {
            w.Tick(Idle);
            Assert.Equal(4, w.Player.Y - last);
            last = w.Player.Y;
        }
        Assert.Equal(14 * T, w.Player.Y);
        Assert.False(w.Player.Falling);

        w.Tick(Idle);
        Assert.Equal(14 * T, w.Player.Y);
    }

    [Fact]
    public void InputIsIgnoredWhileFalling()
    {
        var w = Create("&", "", "", Floor);
        Run(w, Right, 10);
        Assert.True(w.Player.Falling);
        Assert.Equal(0, w.Player.X);
        Assert.Equal(Pose.Fall, w.Player.Pose);
    }

    [Fact]
    public void RunningOffAnEdgeStartsAFall()
    {
        var w = Create("&", "##");
        Run(w, Right, 19);
        Run(w, Idle, 40);
        Assert.Equal(2 * T, w.Player.X);
        Assert.Equal(15 * T, w.Player.Y);
    }

    [Fact]
    public void ClimbsLadderToItsTopAndStops()
    {
        var w = Create("  H", "  H", "& H", Floor);
        Run(w, Right, 24);
        Assert.Equal(2 * T, w.Player.X);

        Run(w, Up, 24);
        Assert.Equal(12 * T, w.Player.Y);
        Assert.Equal(Pose.Climb, w.Player.Pose);

        Run(w, Up, 30);
        Assert.Equal(11 * T, w.Player.Y);
    }

    [Fact]
    public void ClimbsDownLadderAndStopsOnFloor()
    {
        var w = Create("  H", "  H", "& H", Floor);
        Run(w, Right, 24);
        Run(w, Up, 24);
        Run(w, Down, 100);
        Assert.Equal(14 * T, w.Player.Y);
    }

    [Fact]
    public void LadderClimbUsesClimbSpeed()
    {
        var w = Create("  H", "  H", "& H", Floor);
        Run(w, Right, 24);
        w.Tick(Up);
        Assert.Equal(14 * T - 4, w.Player.Y);
    }

    [Fact]
    public void MidLadderSidewaysInOpenAirDropsOffLadder()
    {
        var w = Create("  H", "  H", "& H", Floor);
        Run(w, Right, 24);
        Run(w, Up, 15); // between rows 12 and 13: nothing solid beside the ladder, nothing to land on
        Assert.NotEqual(0, w.Player.Y % T);

        Run(w, Right, 2); // one press is enough: the fall then eases sideways on its own
        Run(w, Idle, 60);
        Assert.Equal(3 * T, w.Player.X);
        Assert.Equal(14 * T, w.Player.Y);
        Assert.False(w.Player.Falling);
    }

    [Fact]
    public void MidLadderSidewaysSlidesToRowWithFloorBeside()
    {
        var w = Create("  H", "  H###", "& H", Floor);
        Run(w, Right, 24);
        Run(w, Up, 30); // between rows 11 and 12; the brick at row 13 makes row 12 the way out
        Assert.NotEqual(0, w.Player.Y % T);

        Run(w, Right, 12);
        Assert.Equal(12 * T, w.Player.Y);
        Assert.True(w.Player.X > 2 * T);
    }

    [Fact]
    public void MidLadderSidewaysIntoWallDoesNothing()
    {
        var w = Create("  H#", "  H#", "& H#", Floor);
        Run(w, Right, 24);
        Run(w, Up, 6);
        int y = w.Player.Y;
        Assert.NotEqual(0, y % T);

        Run(w, Right, 20);
        Assert.Equal(2 * T, w.Player.X);
        Assert.Equal(y, w.Player.Y);
    }

    [Fact]
    public void SlidesToLadderColumnBeforeClimbing()
    {
        var w = Create("  H", "  H", "& H", Floor);
        Run(w, Right, 22); // X = 88, off-column but within half a cell of the ladder
        Assert.Equal(88, w.Player.X);

        Run(w, Up, 2);
        Assert.Equal(2 * T, w.Player.X);
        Assert.Equal(14 * T, w.Player.Y);

        Run(w, Up, 12);
        Assert.Equal(13 * T, w.Player.Y);
    }

    [Fact]
    public void DoesNotSlideTowardColumnWithoutLadder()
    {
        var w = Create("  H", "  H", "& H", Floor);
        Run(w, Right, 2);
        Run(w, Up, 20);
        Assert.Equal(8, w.Player.X);
        Assert.Equal(14 * T, w.Player.Y);
    }

    [Fact]
    public void UpDoesNothingOffLadder()
    {
        var w = Create("&", Floor);
        Run(w, Up, 20);
        Assert.Equal(14 * T, w.Player.Y);
        Assert.Equal(0, w.Player.X);
    }

    [Fact]
    public void DownDoesNothingOnSolidFloor()
    {
        var w = Create("&", Floor);
        Run(w, Down, 20);
        Assert.Equal(14 * T, w.Player.Y);
    }

    [Fact]
    public void WalksAcrossLadderTopWithoutFalling()
    {
        var w = Create("&", "###H###", Floor);
        Run(w, Right, 40);
        Assert.Equal(40 * 4, w.Player.X);
        Assert.Equal(13 * T, w.Player.Y);
    }

    [Fact]
    public void ClimbsDownIntoLadderFromAbove()
    {
        var w = Create("&", "###H###", Floor);
        Run(w, Right, 36); // X = 144, column 3
        Run(w, Down, 12);
        Assert.Equal(14 * T, w.Player.Y);
    }

    [Fact]
    public void HangsAndMovesAlongRope()
    {
        var w = Create("&-----", "#", Floor);
        Run(w, Right, 24); // two cells: now at column 2
        Assert.Equal(2 * T, w.Player.X);
        Assert.Equal(13 * T, w.Player.Y);
        Assert.Equal(Pose.Hang, w.Player.Pose);
    }

    [Fact]
    public void RopeSpeedIsSeparateFromRunSpeed()
    {
        var cfg = new GameConfig { PlayerRopeSpeed = 2 };
        var level = LodeRunner.Core.Levels.LevelParser.Parse(string.Join("\n", Enumerable.Repeat("", 11)
            .Concat(["$", "", "&-----", "#", Floor])));
        var w = new GameWorld(level, cfg, 1);

        Run(w, Right, 6); // X = 24: centre is now inside the first rope cell
        Assert.Equal(24, w.Player.X);
        w.Tick(Right);
        Assert.Equal(26, w.Player.X);
    }

    [Fact]
    public void DownReleasesTheRopeAndFalls()
    {
        var w = Create("&-----", "#", Floor);
        Run(w, Right, 24);
        Run(w, Down, 1);
        Assert.True(w.Player.Falling);

        Run(w, Idle, 40);
        Assert.Equal(14 * T, w.Player.Y);
        Assert.False(w.Player.Falling);
    }

    [Fact]
    public void RunningPastTheRopeEndFalls()
    {
        var w = Create("&-----", "#", Floor);
        Run(w, Right, 80);
        Assert.Equal(Pose.Run, w.Player.Pose);
        Assert.Equal(14 * T, w.Player.Y);
    }

    [Fact]
    public void FallingPlayerCatchesARope()
    {
        var w = Create("&", "", "", "-", "", Floor);
        Run(w, Idle, 100);
        Assert.Equal(13 * T, w.Player.Y);
        Assert.Equal(Pose.Hang, w.Player.Pose);
        Assert.False(w.Player.Falling);
    }

    [Fact]
    public void FallingPlayerLandsOnTopOfALadder()
    {
        var w = Create("&", "", "", "H", "H", Floor);
        Run(w, Idle, 100);
        Assert.Equal(12 * T, w.Player.Y);
        Assert.False(w.Player.Falling);

        Run(w, Down, 24);
        Assert.Equal(14 * T, w.Player.Y);
    }

    [Fact]
    public void HiddenLadderIsNotClimbableUntilRevealed()
    {
        var w = Create("  S", "  S", "& S", Floor);
        Run(w, Right, 24);
        Run(w, Up, 30);
        Assert.Equal(14 * T, w.Player.Y);

        w.Grid.RevealHiddenLadders();
        Run(w, Up, 24);
        Assert.Equal(12 * T, w.Player.Y);
    }

    [Fact]
    public void MisalignedConfigIsRejected()
    {
        var level = LodeRunner.Core.Levels.LevelParser.Parse(
            string.Join("\n", Enumerable.Repeat("", 14).Concat(["$&", Floor])));
        Assert.Throws<ArgumentException>(() => new GameWorld(level, new GameConfig { PlayerRunSpeed = 5 }, 1));
    }

    [Fact]
    public void SameSeedAndInputGiveSameState()
    {
        InputState[] script =
        [
            Right, Right, Right, Up, Up, Down, Left, Left, Idle, Right, Up, Down, Right, Right,
        ];

        var a = Create(7, "  H", "  H", "& H", Floor);
        var b = Create(7, "  H", "  H", "& H", Floor);
        for (int round = 0; round < 12; round++)
        {
            foreach (var input in script)
            {
                Run(a, input, 5);
                Run(b, input, 5);
                Assert.Equal((a.Player.X, a.Player.Y, a.Player.Pose, a.Player.Falling),
                    (b.Player.X, b.Player.Y, b.Player.Pose, b.Player.Falling));
            }
        }
        Assert.Equal(a.TickCount, b.TickCount);
    }
}
