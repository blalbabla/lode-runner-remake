namespace LodeRunner.Core.Levels;

public enum TileType
{
    Empty,
    Brick,
    Concrete,
    Ladder,
    Rope,
    HiddenLadder,
    /// <summary>Looks like a brick, but entities pass through it and fall (the 'X' tile of the original).</summary>
    TrapBrick,
}
