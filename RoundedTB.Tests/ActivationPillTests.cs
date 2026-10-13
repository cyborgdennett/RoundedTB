using Xunit;

namespace RoundedTB.Tests;

public class ActivationPillTests
{
    private static Types.Taskbar Layout() => new()
    {
        TaskbarRect = new() { Left = 0, Top = 1040, Right = 1920, Bottom = 1080 },
        AppListRect = new() { Left = 700, Top = 1040, Right = 1200, Bottom = 1080 },
        TrayRect = new() { Left = 1600, Top = 1040, Right = 1920, Bottom = 1080 }, ScaleFactor = 1
    };
    private static Types.Settings Settings() => new()
    {
        IsDynamic = true,
        DynamicAppListLayout = new() { ShowOnHover = true, HoverActivationPill = true },
        DynamicTrayLayout = new() { ShowOnHover = true, HoverActivationPill = true }
    };

    [Fact]
    public void HiddenSegmentsRequireTheirOwnBottomPillAndStayOpenOverIcons()
    {
        var settings = Settings();
        var current = new Types.Taskbar();
        var measured = Layout();
        SegmentHover.Update(current, measured, new() { x = 950, y = 1060 }, settings: settings, timestamp: 0);
        Assert.False(SegmentHover.ShowApps(current, settings));
        Assert.False(SegmentHover.ShowTray(current, settings));
        SegmentHover.Update(current, measured, new() { x = 950, y = 1079 }, settings: settings, timestamp: 1);
        Assert.True(SegmentHover.ShowApps(current, settings));
        Assert.False(SegmentHover.ShowTray(current, settings));
        SegmentHover.Update(current, measured, new() { x = 750, y = 1050 }, settings: settings, timestamp: 2);
        Assert.True(SegmentHover.ShowApps(current, settings));
        SegmentHover.Update(current, measured, new() { x = 1750, y = 1060 }, settings: settings, timestamp: 3);
        Assert.False(SegmentHover.ShowApps(current, settings));
        Assert.False(SegmentHover.ShowTray(current, settings));
        SegmentHover.Update(current, measured, new() { x = 1760, y = 1079 }, settings: settings, timestamp: 4);
        Assert.True(SegmentHover.ShowTray(current, settings));
        SegmentHover.Update(current, measured, new() { x = 1650, y = 1050 }, settings: settings, timestamp: 5);
        Assert.True(SegmentHover.ShowTray(current, settings));
    }

    [Fact]
    public void PillUsesMonitorScaleAndNegativeCoordinatesAndFitsSegment()
    {
        var taskbar = new Types.Taskbar { TaskbarRect = new() { Left = -1920, Top = -40, Right = 0, Bottom = 0 }, ScaleFactor = 1.5 };
        var settings = new Types.SegmentSettings { HoverPillWidth = 80, HoverPillThickness = 3 };
        var segment = new LocalPInvoke.RECT { Left = -1100, Right = -700 };
        var pill = SegmentHover.ActivationPill(segment, taskbar, settings);
        Assert.Equal(-960, pill.Left);
        Assert.Equal(-840, pill.Right);
        Assert.Equal(-4, pill.Top);
        Assert.Equal(0, pill.Bottom);
        settings.HoverPillWidth = 600;
        pill = SegmentHover.ActivationPill(segment, taskbar, settings);
        Assert.Equal(segment.Left, pill.Left);
        Assert.Equal(segment.Right, pill.Right);
    }

    [Fact]
    public void PreviewAndStartMenuCanRevealWithoutTouchingPill()
    {
        var settings = Settings();
        var current = new Types.Taskbar();
        SegmentHover.Update(current, Layout(), new() { x = 950, y = 800 }, windowPreviewOpen: true, settings: settings, timestamp: 0);
        Assert.True(SegmentHover.ShowApps(current, settings));
        SegmentHover.Update(current, Layout(), new() { x = 950, y = 800 }, startMenuOpen: true, settings: settings, timestamp: 1);
        Assert.True(SegmentHover.ShowApps(current, settings));
        Assert.True(SegmentHover.ShowTray(current, settings));
    }
}
