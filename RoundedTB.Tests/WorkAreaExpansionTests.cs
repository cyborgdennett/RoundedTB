using Xunit;

namespace RoundedTB.Tests;

public class WorkAreaExpansionTests
{
    [Theory]
    [InlineData(1078, 1126, true)]
    [InlineData(1080, 1128, true)]
    [InlineData(1032, 1080, false)]
    [InlineData(1056, 1104, false)]
    public void FullscreenActivationEdgeDoesNotCountAsAVisibleTaskbar(int top, int bottom, bool edge)
        => Assert.Equal(edge, Taskbar.IsFullscreenEdge(
            new LocalPInvoke.RECT { Left = -1920, Right = 0, Top = top, Bottom = bottom },
            new LocalPInvoke.RECT { Left = -1920, Right = 0, Top = 0, Bottom = 1080 }));

    [Theory]
    [InlineData(0, false, true, true)]
    [InlineData(0, true, true, true)]
    [InlineData(0, false, false, false)]
    [InlineData(1, false, true, true)]
    [InlineData(1, true, false, false)]
    public void RequiresOptInIndependentlyOfTaskbarVisibility(int autoHide, bool hover, bool enabled, bool expected)
    {
        var settings = new Types.Settings
        {
            AutoHide = autoHide, IsDynamic = true, ExpandMaximizedWorkArea = enabled,
            DynamicAppListLayout = new Types.SegmentSettings { ShowOnHover = hover }
        };
        Assert.Equal(expected, WorkAreaExpansion.Enabled(settings));
        settings.FillOnMaximise = true;
        Assert.Equal(!enabled, WorkAreaExpansion.RestoreOnMaximize(settings));
    }

    [Theory]
    [InlineData(0x00CF0000, 3, true)]
    [InlineData(0x96CF0000, 3, true)]
    [InlineData(0x80000000, 3, false)]
    [InlineData(0x00CF0000, 1, false)]
    public void KeepsNormalMaximizedWindowsAccessibleWithoutOverridingFullscreenApps(uint style, int state, bool expected)
    {
        Assert.Equal(expected, MaximizedWindowTaskbarAccess.Eligible(style, (LocalPInvoke.ShowWindowCommands)state));
    }
}
