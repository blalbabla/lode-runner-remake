using LodeRunner.Core.Levels;

namespace LodeRunner.Core.Simulation;

/// <summary>
/// Mutable terrain state built from a <see cref="LevelData"/>.
/// Out-of-bounds cells are solid, so levels are enclosed by implicit walls.
/// Holes live on brick cells: Opening and Open read as empty, Refilling reads as brick.
/// </summary>
public sealed class Grid
{
    private readonly TileType[,] _tiles;
    private readonly HoleState[,] _holeState;
    private readonly int[,] _holeLeft;
    private readonly int[,] _holeTotal;
    private readonly List<Cell> _activeHoles = new();
    private bool _hiddenRevealed;

    public int Cols { get; }
    public int Rows { get; }
    public int TileUnits { get; }

    public Grid(LevelData level, int tileUnits)
    {
        Cols = level.Cols;
        Rows = level.Rows;
        TileUnits = tileUnits;
        _tiles = new TileType[Cols, Rows];
        _holeState = new HoleState[Cols, Rows];
        _holeLeft = new int[Cols, Rows];
        _holeTotal = new int[Cols, Rows];
        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
                _tiles[c, r] = level[c, r];
    }

    public bool InBounds(int col, int row) => col >= 0 && col < Cols && row >= 0 && row < Rows;

    /// <summary>Effective terrain. Hidden ladders read as empty until revealed; open holes read as empty.</summary>
    public TileType Tile(int col, int row)
    {
        if (!InBounds(col, row)) return TileType.Concrete;
        var t = _tiles[col, row];
        if (t == TileType.HiddenLadder) return _hiddenRevealed ? TileType.Ladder : TileType.Empty;
        if (t == TileType.TrapBrick) return TileType.Empty;
        if (t == TileType.Brick && _holeState[col, row] is HoleState.Opening or HoleState.Open)
            return PlanningView ? TileType.Brick : TileType.Empty;
        return t;
    }

    /// <summary>
    /// While set, open holes read as intact brick. Guard route planning sets this so guards ignore holes
    /// the player dug and still treat them as floor; the physics then drops them into the pit.
    /// </summary>
    internal bool PlanningView { get; set; }

    public bool IsHiddenLadder(int col, int row) =>
        !_hiddenRevealed && InBounds(col, row) && _tiles[col, row] == TileType.HiddenLadder;

    public bool IsSolid(int col, int row) => Tile(col, row) is TileType.Brick or TileType.Concrete;

    /// <summary>A trap brick: drawn as brick, but walkable-through (entities fall through it).</summary>
    public bool IsTrap(int col, int row) => InBounds(col, row) && _tiles[col, row] == TileType.TrapBrick;

    public bool HiddenRevealed => _hiddenRevealed;

    public void RevealHiddenLadders() => _hiddenRevealed = true;

    public HoleInfo Hole(int col, int row) =>
        InBounds(col, row)
            ? new HoleInfo(_holeState[col, row], _holeLeft[col, row], _holeTotal[col, row])
            : default;

    /// <summary>Holes in the order they were dug.</summary>
    public IReadOnlyList<Cell> ActiveHoles => _activeHoles;

    /// <summary>A hole can start only on an intact brick.</summary>
    internal bool CanStartHole(int col, int row) =>
        InBounds(col, row) && _tiles[col, row] == TileType.Brick && _holeState[col, row] == HoleState.Intact;

    internal void StartHole(int col, int row, int digTicks)
    {
        SetPhase(col, row, HoleState.Opening, digTicks);
        _activeHoles.Add(new Cell(col, row));
    }

    /// <summary>Advances every hole timer by one tick and reports phase changes in dig order.</summary>
    internal void AdvanceHoles(GameConfig config, List<HoleTransition> transitions)
    {
        for (int i = 0; i < _activeHoles.Count;)
        {
            var cell = _activeHoles[i];
            int c = cell.Col, r = cell.Row;

            if (--_holeLeft[c, r] > 0)
            {
                i++;
                continue;
            }

            switch (_holeState[c, r])
            {
                case HoleState.Opening:
                    SetPhase(c, r, HoleState.Open, config.HoleOpenTicks);
                    transitions.Add(new HoleTransition(cell, HoleState.Open));
                    i++;
                    break;
                case HoleState.Open:
                    SetPhase(c, r, HoleState.Refilling, config.RefillTicks);
                    transitions.Add(new HoleTransition(cell, HoleState.Refilling));
                    i++;
                    break;
                default:
                    SetPhase(c, r, HoleState.Intact, 0);
                    _activeHoles.RemoveAt(i);
                    transitions.Add(new HoleTransition(cell, HoleState.Intact));
                    break;
            }
        }
    }

    private void SetPhase(int col, int row, HoleState state, int ticks)
    {
        _holeState[col, row] = state;
        _holeLeft[col, row] = ticks;
        _holeTotal[col, row] = ticks;
    }
}
