namespace LodeRunner.Core;

/// <summary>How guards decide where to go.</summary>
public enum EnemyAiMode
{
    /// <summary>
    /// Original Lode Runner behaviour: run straight at the player on the same row, otherwise head for the
    /// nearest ladder or ledge that brings the guard closest to the player's row. Greedy, no path search.
    /// </summary>
    Classic,

    /// <summary>Breadth-first search over the whole level: always takes the shortest route to the player.</summary>
    Smart,
}
