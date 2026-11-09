namespace LodeRunner.Core.Levels;

public readonly record struct Cell(int Col, int Row);

/// <summary>Immutable parsed level. Gold and spawn marks are stored separately from terrain.</summary>
public sealed class LevelData
{
    private readonly TileType[,] _tiles;

    public string Name { get; }
    public int Cols { get; }
    public int Rows { get; }
    public Cell PlayerStart { get; }
    public IReadOnlyList<Cell> EnemyStarts { get; }
    public IReadOnlyList<Cell> Gold { get; }

    public LevelData(string name, TileType[,] tiles, Cell playerStart,
        IReadOnlyList<Cell> enemyStarts, IReadOnlyList<Cell> gold)
    {
        Name = name;
        Cols = tiles.GetLength(0);
        Rows = tiles.GetLength(1);
        _tiles = (TileType[,])tiles.Clone();
        PlayerStart = playerStart;
        EnemyStarts = enemyStarts;
        Gold = gold;
    }

    public TileType this[int col, int row] => _tiles[col, row];

    public bool InBounds(int col, int row) => col >= 0 && col < Cols && row >= 0 && row < Rows;
}
