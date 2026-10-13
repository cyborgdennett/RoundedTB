using Xunit;

namespace RoundedTB.Tests;

public class TaskbarBoundsTests
{
    [Fact]
    public void FocusedThumbnailAfterClosingOneWindowDoesNotRestoreFullTaskbar()
    {
        Assert.False(Taskbar.IsTaskSwitcherWindow("XamlExplorerHostIslandWindow", true, isThumbnailPreview: true));
        Assert.True(Taskbar.IsTaskSwitcherWindow("XamlExplorerHostIslandWindow", true, isThumbnailPreview: false));
        Assert.True(Taskbar.IsTaskSwitcherWindow("MultitaskingViewFrame", true, isThumbnailPreview: false));
    }

    [Theory]
    [InlineData("StartButton", "ToggleButton", true)]
    [InlineData("TaskViewButton", "ToggleButton", true)]
    [InlineData("Appid: Chrome", "Taskbar.TaskListButtonAutomationPeer", true)]
    [InlineData("SystemTrayIcon", "SystemTray.NormalButton", false)]
    [InlineData("NotifyItemIcon", "SystemTray.NormalButton", false)]
    [InlineData("WidgetsButton", "ToggleButton", false)]
    public void SelectsAppsAndSystemButtonsWithoutIncludingTrayOrWidgets(string id, string className, bool expected)
        => Assert.Equal(expected, TaskbarIconBounds.IsAppListButton(id, className));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-2560, 1)]
    [InlineData(2560, 1.5)]
    public void MeasuredRegionIncludesBothEndsAtDifferentMonitorOriginsAndDpi(int origin, double scale)
    {
        var taskbar = new Types.Taskbar
        {
            TaskbarRect = new LocalPInvoke.RECT { Left = origin, Top = 1392, Right = origin + 2560, Bottom = 1440 },
            AppListRect = new LocalPInvoke.RECT { Left = origin + 1060, Top = 1392, Right = origin + 1501, Bottom = 1440 },
            ScaleFactor = scale
        };
        var settings = new Types.SegmentSettings { MarginLeft = 3, MarginRight = 3, MarginTop = 3, MarginBottom = 3 };
        var region = TaskbarIconBounds.GetRegion(taskbar, settings);
        Assert.True(region.Left <= 1060);
        Assert.True(region.Right >= 1501);
        Assert.True(region.Left >= 0 && region.Right <= 2561);
    }

    [Fact]
    public void UnionIncludesFirstAndLastIconButRejectsEmptyOrOffMonitorElements()
    {
        var taskbar = new LocalPInvoke.RECT { Left = 0, Top = 1392, Right = 2560, Bottom = 1440 };
        LocalPInvoke.RECT bounds = default;
        var found = false;
        TaskbarIconBounds.Include(ref bounds, ref found, taskbar,
            new LocalPInvoke.RECT { Left = 1060, Top = 1392, Right = 1105, Bottom = 1440 });
        TaskbarIconBounds.Include(ref bounds, ref found, taskbar,
            new LocalPInvoke.RECT { Left = 1457, Top = 1392, Right = 1501, Bottom = 1440 });
        TaskbarIconBounds.Include(ref bounds, ref found, taskbar,
            new LocalPInvoke.RECT { Left = -600, Top = 100, Right = -500, Bottom = 148 });
        TaskbarIconBounds.Include(ref bounds, ref found, taskbar, default);
        Assert.True(found);
        Assert.Equal(1060, bounds.Left);
        Assert.Equal(1501, bounds.Right);
    }
    [Theory]
    [InlineData("WidgetsButton", "ToggleButton", true)]
    [InlineData("", "Taskbar.WidgetsButtonAutomationPeer", true)]
    [InlineData("StartButton", "ToggleButton", false)]
    [InlineData("SystemTrayIcon", "SystemTray.NormalButton", false)]
    public void DetectsWidgetsWithoutMistakingOtherButtonsForWidgets(string id, string className, bool expected)
        => Assert.Equal(expected, TaskbarIconBounds.IsWidgetsButton(id, className));

    [Fact]
    public void RemovedOrEmptyWidgetsHaveNoSegmentAndAppearanceChangeTriggersRefresh()
    {
        var before = new Types.Taskbar();
        var after = new Types.Taskbar { WidgetsRect = new LocalPInvoke.RECT { Left = 0, Top = 1392, Right = 160, Bottom = 1440 } };
        Assert.False(before.HasWidgets);
        Assert.True(after.HasWidgets);
        Assert.True(Taskbar.TaskbarRefreshRequired(before, after, true));
        Assert.True(Taskbar.TaskbarRefreshRequired(after, before, true));
    }

    [Theory]
    [InlineData(2, 123, 456, 123)]
    [InlineData(2, 0, 456, 456)]
    [InlineData(6, 123, 456, 123)]
    [InlineData(0, 123, 456, 0)]
    [InlineData(4, 0, 456, 0)]
    [InlineData(2, 0, 0, 0)]
    public void MoveDetectionRequiresAnActiveMoveLoopAndUsesTheMovingWindow(uint flags, int moving, int active, int expected)
    {
        var info = new LocalPInvoke.GUITHREADINFO
        {
            Flags = flags, MoveSize = new System.IntPtr(moving), Active = new System.IntPtr(active)
        };
        Assert.Equal(new System.IntPtr(expected), Taskbar.GetMovingWindow(info));
    }

    [Theory]
    [InlineData("XamlExplorerHostIslandWindow", false, false)]
    [InlineData("XamlExplorerHostIslandWindow", true, true)]
    [InlineData("MultitaskingViewFrame", false, true)]
    [InlineData("TaskSwitcherWnd", false, true)]
    [InlineData("Chrome_WidgetWin_1", true, false)]
    public void SnapPreviewWithoutForegroundFocusDoesNotTriggerTaskSwitcherRestoration(string className, bool foreground, bool expected)
        => Assert.Equal(expected, Taskbar.IsTaskSwitcherWindow(className, foreground));

    [Theory]
    [InlineData("XamlExplorerHostIslandWindow", true)]
    [InlineData("MultitaskingViewFrame", true)]
    [InlineData("TaskSwitcherWnd", true)]
    [InlineData("Chrome_WidgetWin_1", false)]
    [InlineData("ApplicationFrameWindow", false)]
    public void ShellOverlaysAreExcludedFromMaximizedAppDetection(string className, bool expected)
        => Assert.Equal(expected, Taskbar.IsShellOverlayClass(className));

    [Theory]
    [InlineData(1280, 0, true)]
    [InlineData(1280, 8, true)]
    [InlineData(1280, 40, false)]
    [InlineData(8, 0, false)]
    [InlineData(2552, 0, false)]
    [InlineData(1280, -1, false)]
    public void MaximizeSnapTargetRequiresTopEdgeAndExcludesCornerSnaps(int x, int y, bool expected)
    {
        var monitor = new LocalPInvoke.RECT { Left = 0, Top = 0, Right = 2560, Bottom = 1440 };
        Assert.Equal(expected, Taskbar.IsMaximizeSnapTarget(monitor, new LocalPInvoke.POINT { x = x, y = y }, 1));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-2560, 1.5)]
    [InlineData(2560, 2)]
    public void MaximizeSnapTargetUsesMonitorOriginAndDpi(int origin, double scale)
    {
        var work = new LocalPInvoke.RECT { Left = origin, Top = -1440, Right = origin + 2560, Bottom = -48 };
        Assert.True(Taskbar.IsMaximizeSnapTarget(work, new LocalPInvoke.POINT { x = origin + 1280, y = -1435 }, scale));
        Assert.False(Taskbar.IsMaximizeSnapTarget(work, new LocalPInvoke.POINT { x = origin + 1280, y = -1400 }, scale));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-2560, 2.25)]
    [InlineData(2560, 2.5)]
    public void CrowdedTaskbarUsesNativeEdgesInsteadOfReflectingIntoTray(int origin, double scale)
    {
        var bar = new LocalPInvoke.RECT { Left = origin, Right = origin + 2560 };
        var tray = new LocalPInvoke.RECT { Left = origin + 2200, Right = origin + 2560 };
        Assert.True(TaskbarIconBounds.RequiresNativeEdges(bar,
            new LocalPInvoke.RECT { Left = origin + 300, Right = origin + 400 }, tray, scale));
        Assert.False(TaskbarIconBounds.RequiresNativeEdges(bar,
            new LocalPInvoke.RECT { Left = origin + 1060, Right = origin + 1160 }, tray, scale));
        Assert.False(TaskbarIconBounds.RequiresNativeEdges(bar,
            new LocalPInvoke.RECT { Left = origin + 300 }, default, scale));
    }

    [Fact]
    public void MeasuredAppListCanTouchTrayWithoutRejectingUpdate()
    {
        var current = new Types.Taskbar { TaskbarHwnd = new System.IntPtr(1), ScaleFactor = 2.25 };
        var next = new Types.Taskbar
        {
            TaskbarHwnd = current.TaskbarHwnd, HasMeasuredAppListBounds = true,
            TaskbarRect = new LocalPInvoke.RECT { Right = 2560 },
            AppListRect = new LocalPInvoke.RECT { Left = 300, Right = 2200 },
            TrayRect = new LocalPInvoke.RECT { Left = 2200, Right = 2560 }
        };
        Assert.True(Taskbar.CheckDynamicUpdateIsValid(current, next));
        next.AppListRect = new LocalPInvoke.RECT { Left = 300, Right = 2201 };
        Assert.False(Taskbar.CheckDynamicUpdateIsValid(current, next));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(2.25)]
    public void NativeEdgesBeginOneIconBeforePreviousCrowdingThreshold(double scale)
    {
        var bar = new LocalPInvoke.RECT { Right = 2560 };
        var tray = new LocalPInvoke.RECT { Left = 2200, Right = 2560 };
        int iconWidth = (int)System.Math.Ceiling(44 * scale);
        int oldThreshold = 2200 - (int)System.Math.Ceiling(8 * scale);
        int left = 2561 - (oldThreshold - iconWidth);
        var start = new LocalPInvoke.RECT { Left = left, Right = left + iconWidth };
        Assert.True(TaskbarIconBounds.RequiresNativeEdges(bar, start, tray, scale));
        start.Left++;
        start.Right++;
        Assert.False(TaskbarIconBounds.RequiresNativeEdges(bar, start, tray, scale));
    }

}
