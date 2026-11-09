using LodeRunner.Core.Levels;

namespace LodeRunner.Core.Simulation;

/// <summary>Per-mode movement speeds, in units per tick.</summary>
internal readonly record struct Speeds(int Run, int Climb, int Rope, int Fall);

/// <summary>Support that comes from other entities (standing on a head, leaving a pit) rather than terrain.</summary>
internal interface IEntityProbe
{
    bool HasExtraSupport(Entity e);
}

/// <summary>
/// Shared grid movement rules for player and enemies. Position is in integer units, Y grows downward.
///
/// Rules:
///  - Horizontal movement happens only when vertically aligned to a row (Y is a multiple of TileUnits).
///  - Vertical movement (ladders) starts only when horizontally aligned to a column; if the entity is
///    off-column and a ladder is within half a cell, it first slides to that column.
///  - Falling starts only when on a row boundary, with no ladder/rope in the cell and no floor below.
///    It moves at a constant speed, ignores input, and ends on a floor, the top of a ladder, or a rope cell.
///  - Climbing up from a ladder cell is allowed into any non-solid cell above, so the entity steps onto
///    the empty cell over the ladder top and stands there (ladder below supports it).
/// </summary>
internal static class Locomotion
{
    public static void Step(Grid g, Entity e, int dx, int dy, in Speeds s, IEntityProbe? probe = null)
    {
        int t = g.TileUnits;

        if (!e.Falling) e.FallTargetCol = -1;
        if (!e.Falling && e.Y % t == 0 && ShouldFall(g, e, probe))
            e.Falling = true;

        if (e.Falling)
        {
            Fall(g, e, s, probe);
        }
        else if (e.Y % t != 0)
        {
            // Mid-cell: only possible while on a ladder. Vertical input climbs; horizontal input leaves the ladder.
            if (dy != 0) Climb(g, e, dy, s.Climb);
            else if (dx != 0) LeaveLadderSideways(g, e, dx, s, probe);
        }
        else if (dx != 0)
        {
            Horizontal(g, e, dx, s);
        }
        else if (dy != 0)
        {
            VerticalAtRow(g, e, dy, s, probe);
        }

        e.Moved = e.X != e.PrevX || e.Y != e.PrevY;
        UpdatePose(g, e);
    }

    /// <summary>
    /// Entity is on a row boundary and nothing holds it up. Support is judged by the nearest column,
    /// so standing more than half a cell past an edge makes the entity fall.
    /// </summary>
    internal static bool ShouldFall(Grid g, Entity e, IEntityProbe? probe = null, int column = -1)
    {
        int t = g.TileUnits;
        int c = column >= 0 ? column : (e.X + t / 2) / t;
        int r = e.Y / t;

        if (IsSupportedCell(g, c, r)) return false;
        return !(probe?.HasExtraSupport(e) ?? false);
    }

    /// <summary>Terrain alone holds an entity in this cell: a ladder or rope here, or a floor/ladder below.</summary>
    internal static bool IsSupportedCell(Grid g, int c, int r)
    {
        if (g.Tile(c, r) is TileType.Ladder or TileType.Rope) return true;
        return g.Tile(c, r + 1) is TileType.Brick or TileType.Concrete or TileType.Ladder;
    }

    private static void Fall(Grid g, Entity e, in Speeds s, IEntityProbe? probe)
    {
        int t = g.TileUnits;

        // Ease toward the centre of the nearest column (or the column being stepped off to) while falling,
        // at most one run-step per tick.
        int column = e.FallTargetCol;
        if (column >= 0 || e.X % t != 0)
        {
            int target = column >= 0 ? column * t : (e.X + t / 2) / t * t;
            int step = Math.Min(s.Run, Math.Abs(target - e.X));
            e.X += target > e.X ? step : -step;
            if (e.X == target) column = e.FallTargetCol = -1;
        }

        for (int i = 0; i < s.Fall; i++)
        {
            e.Y++;
            if (e.Y % t == 0 && !ShouldFall(g, e, probe, column))
            {
                e.Falling = false;
                e.FallTargetCol = -1;
                break;
            }
        }
    }

    /// <summary>
    /// Horizontal input while between two rows of a ladder. If a floor, ladder or rope waits beside the
    /// ladder on the row above or below, slide along the ladder to that row (nearest first); the step off
    /// then happens on the normal row-aligned path. With nothing to land on, step off and fall from here.
    /// </summary>
    private static void LeaveLadderSideways(Grid g, Entity e, int dir, in Speeds s, IEntityProbe? probe)
    {
        int t = g.TileUnits;
        if (e.X % t != 0) return;

        e.Facing = dir < 0 ? Facing.Left : Facing.Right;
        int c = e.X / t;
        int tc = c + dir;
        int up = e.Y / t;
        int down = up + 1;

        bool upOk = CanStepOffAt(g, c, tc, up);
        bool downOk = CanStepOffAt(g, c, tc, down);
        if (upOk || downOk)
        {
            bool goDown = downOk && (!upOk || e.Y - up * t > t / 2);
            Climb(g, e, goDown ? 1 : -1, s.Climb);
            return;
        }

        if (g.IsSolid(tc, up) || g.IsSolid(tc, down)) return;

        e.Falling = true;
        e.FallTargetCol = tc;
        Fall(g, e, s, probe);
    }

    private static bool CanStepOffAt(Grid g, int c, int tc, int row) =>
        !g.IsSolid(c, row) && !g.IsSolid(tc, row) && IsSupportedCell(g, tc, row);

    private static void Horizontal(Grid g, Entity e, int dir, in Speeds s)
    {
        int t = g.TileUnits;
        e.Facing = dir < 0 ? Facing.Left : Facing.Right;
        int r = e.Y / t;
        int nearest = (e.X + t / 2) / t;
        bool onRope = g.Tile(nearest, r) == TileType.Rope;
        MoveX(g, e, dir, onRope ? s.Rope : s.Run, r);
    }

    /// <summary>Moves along X one unit at a time; stops at walls and on arriving at a column boundary.</summary>
    private static void MoveX(Grid g, Entity e, int dir, int budget, int row)
    {
        int t = g.TileUnits;
        for (int i = 0; i < budget; i++)
        {
            if (e.X % t == 0 && g.IsSolid(e.X / t + dir, row)) break;
            e.X += dir;
            if (e.X % t == 0) break;
        }
    }

    /// <summary>Moves along a ladder one unit at a time; stops on arriving at a row boundary.</summary>
    private static void Climb(Grid g, Entity e, int dir, int budget)
    {
        int t = g.TileUnits;
        for (int i = 0; i < budget; i++)
        {
            e.Y += dir;
            if (e.Y % t == 0) break;
        }
    }

    private static void VerticalAtRow(Grid g, Entity e, int dir, in Speeds s, IEntityProbe? probe)
    {
        int t = g.TileUnits;
        int r = e.Y / t;

        if (e.X % t != 0)
        {
            // Off-column: slide toward the nearest column if it offers a ladder in the wanted direction.
            int n = (e.X + t / 2) / t;
            bool ok = dir < 0 ? CanClimbUp(g, n, r) : CanGoDown(g, n, r);
            if (!ok) return;
            int slide = n * t > e.X ? 1 : -1;
            e.Facing = slide < 0 ? Facing.Left : Facing.Right;
            MoveX(g, e, slide, s.Run, r);
            return;
        }

        int c = e.X / t;
        if (dir < 0)
        {
            if (CanClimbUp(g, c, r)) Climb(g, e, -1, s.Climb);
            return;
        }

        if (g.IsSolid(c, r + 1)) return;
        if (g.Tile(c, r + 1) == TileType.Ladder)
        {
            Climb(g, e, 1, s.Climb);
        }
        else if (g.Tile(c, r) is TileType.Ladder or TileType.Rope)
        {
            // Let go of a rope, or step off a ladder bottom into open air.
            e.Falling = true;
            Fall(g, e, s, probe);
        }
    }

    internal static bool CanClimbUp(Grid g, int c, int r) =>
        g.Tile(c, r) == TileType.Ladder && !g.IsSolid(c, r - 1);

    internal static bool CanGoDown(Grid g, int c, int r) =>
        !g.IsSolid(c, r + 1) &&
        (g.Tile(c, r + 1) == TileType.Ladder || g.Tile(c, r) is TileType.Ladder or TileType.Rope);

    private static void UpdatePose(Grid g, Entity e)
    {
        int t = g.TileUnits;

        if (e.Falling) { e.Pose = Pose.Fall; return; }
        if (e.Y % t != 0) { e.Pose = Pose.Climb; return; }

        int r = e.Y / t;
        int nearest = (e.X + t / 2) / t;
        var here = g.Tile(nearest, r);

        if (here == TileType.Rope) e.Pose = Pose.Hang;
        else if (here == TileType.Ladder && !g.IsSolid(nearest, r + 1)) e.Pose = Pose.Climb;
        else e.Pose = e.X != e.PrevX ? Pose.Run : Pose.Stand;
    }
}
