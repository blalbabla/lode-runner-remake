using LodeRunner.Core.Levels;

namespace LodeRunner.Core.Simulation;

/// <summary>
/// Breadth-first search over grid cells using the same movement rules as <see cref="Locomotion"/>.
/// Neighbour order is fixed, so results are deterministic.
/// If the target cannot be reached, the reachable cell nearest to it (Manhattan) is used instead.
/// </summary>
internal sealed class EnemyPathfinder
{
    private readonly int _cols;
    private readonly int _rows;
    private readonly int[] _parent;
    private readonly int[] _queue;
    private readonly int[] _stamp;
    private int _current;

    public EnemyPathfinder(int cols, int rows)
    {
        _cols = cols;
        _rows = rows;
        _parent = new int[cols * rows];
        _queue = new int[cols * rows];
        _stamp = new int[cols * rows];
    }

    /// <summary>
    /// First move (dx, dy) from <paramref name="from"/> toward <paramref name="to"/>; (0, 0) if none.
    /// <paramref name="extraSupport"/> reports cells held up by something other than terrain (heads, pit exits).
    /// </summary>
    public (int Dx, int Dy) NextStep(Grid g, Cell from, Cell to, Func<int, int, bool> extraSupport)
    {
        if (!g.InBounds(from.Col, from.Row) || !g.InBounds(to.Col, to.Row) || from == to)
            return (0, 0);

        _current++;
        int start = Index(from.Col, from.Row);
        int goal = Index(to.Col, to.Row);

        int head = 0, tail = 0;
        _queue[tail++] = start;
        _stamp[start] = _current;
        _parent[start] = -1;

        int best = start;
        int bestDist = Distance(from.Col, from.Row, to);

        while (head < tail)
        {
            int node = _queue[head++];
            if (node == goal)
            {
                best = node;
                break;
            }

            int c = node % _cols;
            int r = node / _cols;

            int dist = Distance(c, r, to);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = node;
            }

            if (!Locomotion.IsSupportedCell(g, c, r) && !extraSupport(c, r))
            {
                Visit(node, c, r + 1, ref tail);
                continue;
            }

            if (!g.IsSolid(c - 1, r)) Visit(node, c - 1, r, ref tail);
            if (!g.IsSolid(c + 1, r)) Visit(node, c + 1, r, ref tail);
            if (Locomotion.CanClimbUp(g, c, r)) Visit(node, c, r - 1, ref tail);
            if (Locomotion.CanGoDown(g, c, r)) Visit(node, c, r + 1, ref tail);
        }

        if (best == start) return (0, 0);

        // Walk back to the node whose parent is the start.
        int step = best;
        while (_parent[step] != start) step = _parent[step];

        return (step % _cols - from.Col, step / _cols - from.Row);
    }

    private void Visit(int parent, int c, int r, ref int tail)
    {
        if (c < 0 || c >= _cols || r < 0 || r >= _rows) return;
        int n = Index(c, r);
        if (_stamp[n] == _current) return;
        _stamp[n] = _current;
        _parent[n] = parent;
        _queue[tail++] = n;
    }

    private int Index(int c, int r) => r * _cols + c;

    private static int Distance(int c, int r, Cell to) => Math.Abs(c - to.Col) + Math.Abs(r - to.Row);
}
