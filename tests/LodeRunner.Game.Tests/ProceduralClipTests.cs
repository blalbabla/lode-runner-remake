using LodeRunner.Game.Audio;

namespace LodeRunner.Game.Tests;

public sealed class ProceduralClipTests
{
    [Fact]
    public void GoldGeneratorReturnsStereoFloatClip()
    {
        var clip = ProceduralSfx.CreateGold();

        Assert.Equal(clip.Left.Length, clip.Right.Length);
        Assert.Equal(33075, clip.Frames);
        Assert.All(clip.Left, sample => Assert.InRange(sample, -1f, 1f));
        Assert.All(clip.Right, sample => Assert.InRange(sample, -1f, 1f));
    }

    [Fact]
    public void FootstepVariantsAreDistinct()
    {
        var first = ProceduralFootsteps.CreateClip(ProceduralFootsteps.StepKind.RunLeft, 0);
        var second = ProceduralFootsteps.CreateClip(ProceduralFootsteps.StepKind.RunLeft, 1);

        Assert.True(first.Left.Where((sample, index) => sample != second.Left[index]).Any());
    }
}
