using Xunit;

namespace RoundedTB.Tests;

public class CenteredTaskbarBoundsTests
{
    private static LocalPInvoke.RECT Bounds(int left, int right) => new()
    {
        Left = left, Right = right, Top = 1392, Bottom = 1440
    };

    [Fact]
    public void RediscoveryDuringGroupRemovalDoesNotShiftMonitorCenter()
    {
        var follower = new CenteredTaskbarBounds();
        // Start has reached its new position; the removed last button still
        // reports its old edge, one button beyond the surviving group.
        follower.Initialize(Bounds(1060, 1545), 0, stableCenter: 1280.5);
        var result = follower.Follow(Bounds(1060, 1105), 8, 1);
        Assert.Equal(1060, result.Left);
        Assert.Equal(1501, result.Right);
        follower.Initialize(Bounds(-860, -375), 16, stableCenter: -639.5);
        result = follower.Follow(Bounds(-860, -815), 24, 1);
        Assert.Equal(-419, result.Right);
    }

    [Fact]
    public void OpeningIgnoresPrematureLayoutJumpAndFollowsAnimatedStartWithoutReversal()
    {
        var follower = new CenteredTaskbarBounds();
        follower.Initialize(Bounds(1060, 1501), 0);
        Assert.Equal(1060, follower.Follow(Bounds(1038, 1479), 8, 1).Left);
        int previous = 1060;
        long time = 100;
        foreach (int left in new[] { 1059, 1058, 1059, 1051, 1046, 1038 })
        {
            var result = follower.Follow(Bounds(left, 1479), time += 16, 1);
            Assert.InRange(result.Left, 1038, previous);
            Assert.Equal(2561, result.Left + result.Right);
            previous = result.Left;
        }
        Assert.Equal(1038, previous);
    }

    [Fact]
    public void ClosingIgnoresDestinationUntilCompositionRestartsAndBothEdgesShrinkTogether()
    {
        var follower = new CenteredTaskbarBounds();
        follower.Initialize(Bounds(1038, 1523), 0);
        Assert.Equal(1038, follower.Follow(Bounds(1060, 1523), 10, 1).Left);
        Assert.Equal(1038, follower.Follow(Bounds(1060, 1479), 200, 1).Left);
        Assert.Equal(1038, follower.Follow(Bounds(1038, 1495), 300, 1).Left);
        int previous = 1038;
        long time = 310;
        foreach (int left in new[] { 1039, 1044, 1043, 1049, 1055, 1058, 1060 })
        {
            var result = follower.Follow(Bounds(left, 1499), time += 16, 1);
            Assert.InRange(result.Left, previous, 1060);
            Assert.Equal(2561, result.Left + result.Right);
            previous = result.Left;
        }
        Assert.Equal(1060, previous);
    }

    [Fact]
    public void ClosingDuringOpeningRetargetsWithoutContinuingTheOldTransition()
    {
        var follower = new CenteredTaskbarBounds();
        follower.Initialize(Bounds(1060, 1501), 0);
        follower.Follow(Bounds(1038, 1479), 10, 1);
        Assert.Equal(1059, follower.Follow(Bounds(1059, 1479), 30, 1).Left);
        follower.ExpectDirection(1);
        Assert.Equal(1059, follower.Follow(Bounds(1038, 1523), 40, 1).Left);
        Assert.Equal(1060, follower.Follow(Bounds(1060, 1501), 56, 1).Left);
        Assert.False(follower.Follow(Bounds(1060, 1501), 200, 1).Right > 1501);
    }

    [Fact]
    public void MissingAnimationSettlesWithoutOvershootingAndResumeUsesCurrentLayout()
    {
        var follower = new CenteredTaskbarBounds();
        follower.Initialize(Bounds(1060, 1501), 0);
        follower.Follow(Bounds(1038, 1523), 8, 1);
        int previous = 1060;
        for (long time = 16; time < 1000; time += 8)
        {
            int left = follower.Follow(Bounds(1038, 1523), time, 1).Left;
            Assert.InRange(left, 1038, previous);
            previous = left;
        }
        Assert.Equal(1038, previous);
        Assert.Equal(1060, follower.Follow(Bounds(1060, 1501), 3000, 1).Left);
    }
}
