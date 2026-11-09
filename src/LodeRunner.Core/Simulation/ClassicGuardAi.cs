using LodeRunner.Core.Levels;

namespace LodeRunner.Core.Simulation;

/// <summary>
/// Guard decision rules modelled on the original game. Greedy and local, no path search:
///  1. Same row as the player with a clear, supported run between them: run straight at the player.
///  2. Otherwise look for places where the guard can change rows: a ladder down or up in its own column or
///     further along the row, a rope it can drop from, or a ledge edge to fall off. Each is rated by how
///     close the best row it leads to gets to the player's row.
///  3. Take the best rating. Ties go to the spot whose column is nearest the player, then the nearer spot,
///     then going down before going up.
/// A guard that finds no such spot stands still. Like the original, a guard can shuffle on a ladder when
/// the player stands on a row it cannot reach.
/// Decisions happen only on cell boundaries, so the result depends only on the current layout.
/// </summary>
internal sealed class ClassicGuardAi
{
    /// <summary>First move (dx, dy) from <paramref name="from"/> toward <paramref name="to"/>; (0, 0) if none.</summary>
    public (int Dx, int Dy) NextStep(Grid g, Cell from, Cell to, Func<int, int, bool> extraSupport)
    {
        if (!g.InBounds(from.Col, from.Row) || !g.InBounds(to.Col, to.Row) || from == to)
            return (0, 0);

        int x = from.Col, y = from.Row;

        // Nothing holds the guard up: it is falling, there is no choice to make.
        if (!Locomotion.IsSupportedCell(g, x, y) && !extraSupport(x, y))
            return (0, 0);

        if (y == to.Row && CanRunTo(g, x, y, to.Col))
            return (Math.Sign(to.Col - x), 0);

        var best = new Best();

        // Ways up or down in the guard's own column.
        EvaluateColumn(g, x, y, to, dist: 0, firstDx: 0, ref best);

        // Walk outward along the row; every cell passed may offer a way up or down.
        foreach (int d in (ReadOnlySpan<int>)[-1, 1])
        {
            for (int c = x + d; ; c += d)
            {
                if (g.IsSolid(c, y)) break;

                int dist = Math.Abs(c - x);
                if (!Locomotion.IsSupportedCell(g, c, y))
                {
                    // Stepping into this cell means falling out of it, down to the first support.
                    int land = y;
                    while (!Locomotion.IsSupportedCell(g, c, land)) land++;
                    best.Offer(Math.Abs(land - to.Row), Math.Abs(c - to.Col), dist, down: true, d, 0);
                    break;
                }

                EvaluateColumn(g, c, y, to, dist, d, ref best);
            }
        }

        return best.Found ? (best.Dx, best.Dy) : (0, 0);
    }

    private static void EvaluateColumn(Grid g, int c, int r, Cell player, int dist, int firstDx, ref Best best)
    {
        int colDist = Math.Abs(c - player.Col);

        if (Locomotion.CanGoDown(g, c, r))
        {
            int rating = int.MaxValue;
            int rr = r;
            while (Locomotion.CanGoDown(g, c, rr))
            {
                rr++;
                while (!Locomotion.IsSupportedCell(g, c, rr)) rr++; // drops through open air to the next support
                rating = Math.Min(rating, Math.Abs(rr - player.Row));
            }
            best.Offer(rating, colDist, dist, down: true, firstDx, firstDx == 0 ? 1 : 0);
        }

        if (Locomotion.CanClimbUp(g, c, r))
        {
            int rating = int.MaxValue;
            int rr = r;
            while (Locomotion.CanClimbUp(g, c, rr))
            {
                rr--;
                rating = Math.Min(rating, Math.Abs(rr - player.Row));
            }
            best.Offer(rating, colDist, dist, down: false, firstDx, firstDx == 0 ? -1 : 0);
        }
    }

    /// <summary>Every cell between the guard and the target is open and holds a guard up; the target cell itself only has to be open.</summary>
    private static bool CanRunTo(Grid g, int x, int y, int targetCol)
    {
        int d = Math.Sign(targetCol - x);
        for (int c = x + d; ; c += d)
        {
            if (g.IsSolid(c, y)) return false;
            if (c == targetCol) return true;
            if (!Locomotion.IsSupportedCell(g, c, y)) return false;
        }
    }

    private struct Best
    {
        private int _rating, _colDist, _dist, _down;
        public bool Found;
        public int Dx, Dy;

        public void Offer(int rating, int colDist, int dist, bool down, int dx, int dy)
        {
            int dn = down ? 0 : 1;
            if (Found && (rating, colDist, dist, dn).CompareTo((_rating, _colDist, _dist, _down)) >= 0) return;

            Found = true;
            _rating = rating;
            _colDist = colDist;
            _dist = dist;
            _down = dn;
            Dx = dx;
            Dy = dy;
        }
    }
}
