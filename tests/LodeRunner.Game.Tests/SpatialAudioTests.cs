using LodeRunner.Core.Levels;
using LodeRunner.Core.Simulation;
using LodeRunner.Game.Audio;

namespace LodeRunner.Game.Tests;

public sealed class SpatialAudioTests
{
    [Fact]
    public void NearSourceIsLouderThanDistantSource()
    {
        var grid = Grid("     ");
        var near = SpatialAudio.Evaluate(grid, new Cell(0, 0), new Cell(1, 0), 0f, false);
        var far = SpatialAudio.Evaluate(grid, new Cell(0, 0), new Cell(4, 0), 0f, false);

        Assert.True(near.Gain > far.Gain);
    }

    [Fact]
    public void SolidCellReducesGain()
    {
        var open = Grid("   ");
        var blocked = Grid(" # ");
        var clear = SpatialAudio.Evaluate(open, new Cell(0, 0), new Cell(2, 0), 0f, false);
        var muffled = SpatialAudio.Evaluate(blocked, new Cell(0, 0), new Cell(2, 0), 0f, false);

        Assert.True(muffled.Gain < clear.Gain);
        Assert.True(muffled.Occlusion > 0f);
    }

    [Fact]
    public void FirstPersonPanUsesCameraYaw()
    {
        var grid = Grid("   ");
        var right = SpatialAudio.Evaluate(grid, new Cell(1, 0), new Cell(2, 0), MathF.PI / 2f, true);
        var left = SpatialAudio.Evaluate(grid, new Cell(1, 0), new Cell(0, 0), MathF.PI / 2f, true);

        Assert.True(right.Pan > 0.5f);
        Assert.True(left.Pan < 0.5f);
    }

    private static Grid Grid(string row)
    {
        var tiles = new TileType[row.Length, 1];
        for (int col = 0; col < row.Length; col++)
            tiles[col, 0] = row[col] == '#' ? TileType.Brick : TileType.Empty;
        return new Grid(new LevelData("audio", tiles, new Cell(0, 0), [], []), 48);
    }
}
