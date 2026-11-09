using LodeRunner.Core.Levels;
using LodeRunner.Core.Simulation;
using static LodeRunner.Core.Tests.TestWorld;

namespace LodeRunner.Core.Tests;

public class TrapBrickTests
{
    private static readonly string Floor = new('#', 28);
    private static readonly InputState Right = new(Right: true);
    private static readonly InputState Idle = default;

    [Fact]
    public void ParserReadsTrapAsTrapBrick()
    {
        var w = Create("&X", Floor);
        Assert.Equal(TileType.TrapBrick, w.Level[1, 14]);
        Assert.True(w.Grid.IsTrap(1, 14));
    }

    [Fact]
    public void TrapIsNotSolid()
    {
        var w = Create("&X", Floor);
        Assert.False(w.Grid.IsSolid(1, 14));
    }

    [Fact]
    public void PlayerWalkingOntoTrapFallsThrough()
    {
        // Row 14 holds the trap, row 15 is a floor with a gap below the trap row 13 to land on row 14's floor.
        var w = Create("&X", "###  ###", Floor);
        for (int i = 0; i < 40; i++) w.Tick(Right);
        for (int i = 0; i < 60; i++) w.Tick(Idle);
        Assert.True(w.Player.Y >= 14 * T);
    }

    [Fact]
    public void TrapCannotBeDug()
    {
        var w = Create("&X", Floor);
        w.Tick(new InputState(DigRight: true));
        Assert.False(w.Grid.Hole(1, 15).State != HoleState.Intact);
        Assert.Equal(Pose.Stand, w.Player.Pose);
    }
}
