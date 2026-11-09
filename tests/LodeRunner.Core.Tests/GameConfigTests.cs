using LodeRunner.Core;

namespace LodeRunner.Core.Tests;

public class GameConfigTests
{
    [Fact]
    public void DefaultSpeedsDivideTileUnits()
    {
        Assert.True(new GameConfig().AreSpeedsAligned());
    }

    [Fact]
    public void EnemiesAreSlowerThanPlayerOnFoot()
    {
        var config = new GameConfig();
        Assert.True(config.EnemyRunSpeed < config.PlayerRunSpeed);
        Assert.True(config.EnemyClimbSpeed < config.PlayerClimbSpeed);
    }

    [Fact]
    public void MisalignedSpeedIsDetected()
    {
        var config = new GameConfig { PlayerRunSpeed = 5 };
        Assert.False(config.AreSpeedsAligned());
    }

    [Fact]
    public void ZeroSpeedIsRejected()
    {
        var config = new GameConfig { FallSpeed = 0 };
        Assert.False(config.AreSpeedsAligned());
    }
}
