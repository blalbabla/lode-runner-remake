namespace LodeRunner.Core.Simulation;

public sealed class Player : Entity
{
    /// <summary>Ticks the player stays frozen while digging. Zero when free to act.</summary>
    public int DigTicksLeft { get; internal set; }

    internal Player(int x, int y) : base(x, y)
    {
    }
}
