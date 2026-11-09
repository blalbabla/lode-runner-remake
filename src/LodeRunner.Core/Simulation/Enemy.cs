namespace LodeRunner.Core.Simulation;

public enum EnemyState
{
    /// <summary>Free to move; chases the player.</summary>
    Active,
    /// <summary>Stuck at the bottom of an open hole.</summary>
    Trapped,
    /// <summary>Rising out of a hole toward the cell above it.</summary>
    ClimbingOut,
    /// <summary>Crushed by a closing hole; waiting to respawn on the top row.</summary>
    Dead,
}

public sealed class Enemy : Entity
{
    public int Id { get; }
    public EnemyState State { get; internal set; } = EnemyState.Active;

    /// <summary>True while carrying a gold bar picked up from the level.</summary>
    public bool HasGold { get; internal set; }

    /// <summary>Ticks until the carried gold is dropped.</summary>
    public int GoldDropTicks { get; internal set; }

    /// <summary>Ticks left in the current Trapped, ClimbingOut or Dead phase.</summary>
    public int StateTicksLeft { get; internal set; }

    public bool IsAlive => State != EnemyState.Dead;

    // Direction chosen at the last cell boundary; kept until the next one.
    internal int DirX { get; set; }
    internal int DirY { get; set; }

    internal int ClimbStartY { get; set; }
    internal int ClimbTotal { get; set; }

    // Cell of the hole just climbed out of: that hole cell supports the enemy until it steps away.
    internal int PitCol { get; set; } = -1;
    internal int PitRow { get; set; }

    // Cell where gold was last dropped: no pickup there until the enemy leaves it.
    internal int DropCol { get; set; } = -1;
    internal int DropRow { get; set; } = -1;

    // Classic AI anti-dithering: the last two distinct cells a decision was made in, and how many decisions
    // are still left to the path search after the greedy rules tried to send the guard straight back.
    internal int CurDecCol { get; set; } = -1;
    internal int CurDecRow { get; set; } = -1;
    internal int PrevDecCol { get; set; } = -1;
    internal int PrevDecRow { get; set; } = -1;
    internal int SearchDecisionsLeft { get; set; }

    internal Enemy(int id, int x, int y) : base(x, y)
    {
        Id = id;
    }
}
