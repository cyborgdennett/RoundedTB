using Xunit;

namespace RoundedTB.Tests;

public class SegmentRestorationTests
{
    [Fact]
    public void RevealsConfiguredSegmentsWithoutEnablingDisabledWidgetsAndReturnsToHover()
    {
        var settings = new Types.Settings
        {
            IsDynamic = true, ExpandMaximizedWorkArea = true, ShowTray = true,
            DynamicAppListLayout = new() { ShowOnHover = true },
            DynamicTrayLayout = new() { ShowOnHover = true },
            DynamicWidgetsLayout = new()
        };
        var taskbar = new Types.Taskbar { RevealAllSegments = true };
        SegmentHover.Update(taskbar, new(), new() { x = -100, y = -100 }, settings: settings, timestamp: 0);
        Assert.True(SegmentHover.ShowApps(taskbar, settings));
        Assert.True(SegmentHover.ShowTray(taskbar, settings));
        Assert.False(SegmentHover.ShowWidgets(taskbar, settings));
        settings.DynamicWidgetsLayout.ShowOnHover = true;
        Assert.True(SegmentHover.ShowWidgets(taskbar, settings));
        taskbar.RevealAllSegments = false;
        SegmentHover.Update(taskbar, new(), new() { x = -100, y = -100 }, settings: settings, timestamp: 1);
        Assert.False(SegmentHover.ShowApps(taskbar, settings));
        Assert.False(SegmentHover.ShowTray(taskbar, settings));
        Assert.False(SegmentHover.ShowWidgets(taskbar, settings));
    }
}
