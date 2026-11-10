using LodeRunner.Core.Levels;
using LodeRunner.Core.Simulation;

namespace LodeRunner.Game.Audio;

public readonly record struct SpatialMix(float Gain, float Pan, float Occlusion);

/// <summary>Pure spatial mixing rules for the grid-backed dungeon.</summary>
public static class SpatialAudio
{
    public static SpatialMix Evaluate(Grid grid, Cell listener, Cell source, float listenerYaw, bool firstPerson)
    {
        float dx = source.Col - listener.Col;
        float dy = source.Row - listener.Row;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        float gain = 1f / (1f + 0.075f * distance * distance);

        float pan;
        if (firstPerson)
        {
            float right = dx * MathF.Sin(listenerYaw);
            pan = 0.5f + 0.38f * right / MathF.Max(1f, distance);
        }
        else
        {
            pan = 0.5f + 0.38f * dx / MathF.Max(1f, distance);
        }

        int walls = CountSolidCells(grid, listener, source);
        float occlusion = Math.Clamp(walls * 0.20f, 0f, 0.70f);
        gain *= 1f - occlusion;
        return new SpatialMix(gain, Math.Clamp(pan, 0.10f, 0.90f), occlusion);
    }

    private static int CountSolidCells(Grid grid, Cell from, Cell to)
    {
        int x = from.Col;
        int y = from.Row;
        int dx = Math.Abs(to.Col - x);
        int sx = x < to.Col ? 1 : -1;
        int dy = -Math.Abs(to.Row - y);
        int sy = y < to.Row ? 1 : -1;
        int error = dx + dy;
        int walls = 0;

        while (x != to.Col || y != to.Row)
        {
            int twiceError = error * 2;
            if (twiceError >= dy)
            {
                error += dy;
                x += sx;
            }
            if (twiceError <= dx)
            {
                error += dx;
                y += sy;
            }

            if ((x != to.Col || y != to.Row) && grid.Tile(x, y) is TileType.Brick or TileType.Concrete)
                walls++;
        }

        return walls;
    }
}
