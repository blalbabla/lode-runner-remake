using System.Numerics;

namespace LodeRunner.Game.Rendering;

/// <summary>
/// Grid to world mapping. +X right, +Y up, +Z toward the camera; one cell is one world unit.
/// Only the renderer knows these formulas.
/// </summary>
public static class GridCoords
{
    public static Vector3 CellCenter(int col, int row, int rows) =>
        new(col + 0.5f, rows - 1 - row + 0.5f, 0f);

    /// <summary>
    /// Centre of an entity box given its simulation position in units (Y grows downward),
    /// interpolated between the previous and current tick by <paramref name="alpha"/> in [0, 1].
    /// </summary>
    public static Vector3 EntityCenter(int prevX, int prevY, int x, int y, float alpha, int tileUnits, int rows)
    {
        float ix = prevX + (x - prevX) * alpha;
        float iy = prevY + (y - prevY) * alpha;
        return new Vector3(ix / tileUnits + 0.5f, rows - iy / tileUnits - 0.5f, 0f);
    }
}
