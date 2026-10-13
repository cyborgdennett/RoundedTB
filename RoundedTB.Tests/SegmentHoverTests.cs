using Xunit;

namespace RoundedTB.Tests;

public class SegmentHoverTests
{
    [Theory]
    [InlineData("XamlExplorerHostIslandWindow", false, false, false)]
    [InlineData("XamlExplorerHostIslandWindow", true, false, true)]
    [InlineData("XamlExplorerHostIslandWindow", true, true, false)]
    [InlineData("TaskListThumbnailWnd", false, true, false)]
    [InlineData("TaskListThumbnailWnd", false, false, true)]
    [InlineData("Xaml_WindowedPopupClass", false, true, false)]
    public void MovingWindowsAndUnidentifiedShellOverlaysDoNotCountAsThumbnails(string windowClass, bool knownHost, bool moving, bool expected)
        => Assert.Equal(expected, Taskbar.IsThumbnailPreviewCandidate(windowClass, knownHost, moving));

    [Theory]
    [InlineData("TopLevelWindowForOverflowXamlIsland", "explorer", true)]
    [InlineData("NotifyIconOverflowWindow", "explorer", true)]
    [InlineData("Shell_InputSwitchTopLevelWindow", "explorer", true)]
    [InlineData("Shell_InputSwitchTopLevelWindow", "Chrome", false)]
    [InlineData("Windows.UI.Core.CoreWindow", "ShellExperienceHost", true)]
    [InlineData("ControlCenterWindow", "ShellHost", true)]
    [InlineData("Windows.UI.Core.CoreWindow", "StartMenuExperienceHost", false)]
    [InlineData("XamlExplorerHostIslandWindow", "explorer", false)]
    [InlineData("Windows.UI.Core.CoreWindow", "CalculatorApp", false)]
    public void RecognizesTrayPanelsWithoutLocalizedWindowTitles(string windowClass, string process, bool expected)
        => Assert.Equal(expected, Taskbar.IsTrayPopupHost(windowClass, process));

    [Fact]
    public void OpenTrayPanelKeepsOnlyTrayVisibleAndDelayBeginsAfterClosing()
    {
        var settings = new Types.Settings
        {
            IsDynamic = true,
            DynamicAppListLayout = new() { ShowOnHover = true },
            DynamicTrayLayout = new() { ShowOnHover = true, HoverActivationPill = true, HoverHideDelaySeconds = 1 },
            DynamicWidgetsLayout = new() { ShowOnHover = true }
        };
        var current = new Types.Taskbar();
        var measured = new Types.Taskbar();
        var cursor = new LocalPInvoke.POINT { x = 100, y = 100 };
        SegmentHover.Update(current, measured, cursor, settings: settings, timestamp: 0, trayPopupOpen: true);
        SegmentHover.Update(current, measured, cursor, settings: settings, timestamp: 5000, trayPopupOpen: true);
        Assert.True(SegmentHover.ShowTray(current, settings));
        Assert.False(SegmentHover.ShowApps(current, settings));
        Assert.False(SegmentHover.ShowWidgets(current, settings));
        SegmentHover.Update(current, measured, cursor, settings: settings, timestamp: 6000);
        Assert.True(SegmentHover.ShowTray(current, settings));
        SegmentHover.Update(current, measured, cursor, settings: settings, timestamp: 7000);
        Assert.False(SegmentHover.ShowTray(current, settings));
    }

    [Fact]
    public void ModernPopupMustBeBesideAppSegmentAndNotAFullScreenOverlay()
    {
        var taskbar = new Types.Taskbar
        {
            TaskbarRect = new LocalPInvoke.RECT { Left = -1920, Top = 1040, Right = 0, Bottom = 1080 },
            AppListRect = new LocalPInvoke.RECT { Left = -1200, Right = -700 }, ScaleFactor = 1
        };
        var popup = new LocalPInvoke.RECT { Left = -1100, Top = 800, Right = -800, Bottom = 1032 };
        Assert.True(Taskbar.IsPopupAdjacentToApps(popup, taskbar));
        popup.Bottom = 1000; // Pointer may cross a gap while the flyout remains open.
        Assert.True(Taskbar.IsPopupAdjacentToApps(popup, taskbar));
        popup.Left = -400; popup.Right = -100; // Tray flyouts don't reveal apps.
        Assert.False(Taskbar.IsPopupAdjacentToApps(popup, taskbar));
        popup = new LocalPInvoke.RECT { Left = -1920, Top = 0, Right = 0, Bottom = 1040 };
        Assert.False(Taskbar.IsPopupAdjacentToApps(popup, taskbar));
        Assert.True(Taskbar.IsTaskbarPopupClass("Xaml_WindowedPopupClass"));
        Assert.False(Taskbar.PreviewRequiresAdjacentBounds("XamlExplorerHostIslandWindow"));
        Assert.True(Taskbar.PreviewRequiresAdjacentBounds("Xaml_WindowedPopupClass"));
        Assert.True(Taskbar.IsTaskbarPopupClass("TaskListThumbnailWnd"));
        Assert.False(Taskbar.IsTaskbarPopupClass("ApplicationFrameWindow"));
    }

    [Fact]
    public void ThumbnailPreviewKeepsTaskbarVisibleAfterPointerLeavesIcons()
    {
        var settings = new Types.Settings
        {
            IsDynamic = true,
            DynamicAppListLayout = new Types.SegmentSettings { ShowOnHover = true },
            DynamicTrayLayout = new Types.SegmentSettings { ShowOnHover = true },
            DynamicWidgetsLayout = new Types.SegmentSettings { ShowOnHover = true }
        };
        var taskbar = new Types.Taskbar();
        var measured = new Types.Taskbar();
        var cursor = new LocalPInvoke.POINT { x = 50, y = 50 };
        Assert.True(SegmentHover.Update(taskbar, measured, cursor, windowPreviewOpen: true));
        Assert.False(taskbar.AppListHovered);
        Assert.True(SegmentHover.ShowApps(taskbar, settings));
        Assert.False(SegmentHover.ShowTray(taskbar, settings));
        Assert.False(SegmentHover.ShowWidgets(taskbar, settings));
        Assert.False(SegmentHover.Update(taskbar, measured, cursor, windowPreviewOpen: true));
        Assert.True(SegmentHover.Update(taskbar, measured, cursor));
        Assert.False(SegmentHover.ShowApps(taskbar, settings));
    }

    [Theory]
    [InlineData("StartMenuExperienceHost", true)]
    [InlineData("SearchHost", true)]
    [InlineData("SearchApp", true)]
    [InlineData("explorer", false)]
    [InlineData("Chrome", false)]
    public void RecognizesStartAndSearchMenuHosts(string process, bool expected)
        => Assert.Equal(expected, Taskbar.IsStartMenuProcess(process));

    [Fact]
    public void StartMenuTemporarilyRevealsHiddenTrayWithoutChangingPreferences()
    {
        var settings = new Types.Settings { IsDynamic = true, ShowTray = false };
        var taskbar = new Types.Taskbar();
        var measured = new Types.Taskbar();
        var cursor = new LocalPInvoke.POINT { x = -100, y = -100 };
        SegmentHover.Update(taskbar, measured, cursor, settings: settings, timestamp: 0);
        Assert.False(SegmentHover.ShowTray(taskbar, settings));
        SegmentHover.Update(taskbar, measured, cursor, startMenuOpen: true, settings: settings, timestamp: 100);
        Assert.True(SegmentHover.ShowTray(taskbar, settings));
        Assert.Equal(1, taskbar.TrayHoverTransition.Progress);
        Assert.False(settings.ShowTray);
        SegmentHover.Update(taskbar, measured, cursor, settings: settings, timestamp: 200);
        Assert.False(SegmentHover.ShowTray(taskbar, settings));
    }

    [Fact]
    public void StartMenuRevealsTaskbarAndTrayAndClosingRestoresHoverBehavior()
    {
        var settings = new Types.Settings
        {
            IsDynamic = true,
            DynamicAppListLayout = new Types.SegmentSettings { ShowOnHover = true },
            DynamicTrayLayout = new Types.SegmentSettings { ShowOnHover = true },
            DynamicWidgetsLayout = new Types.SegmentSettings { ShowOnHover = true }
        };
        var taskbar = new Types.Taskbar();
        var measured = new Types.Taskbar();
        var cursor = new LocalPInvoke.POINT { x = 100, y = 100 };
        Assert.False(SegmentHover.ShowApps(taskbar, settings));
        Assert.True(SegmentHover.Update(taskbar, measured, cursor, startMenuOpen: true));
        Assert.True(SegmentHover.ShowApps(taskbar, settings));
        Assert.True(SegmentHover.ShowTray(taskbar, settings));
        Assert.False(SegmentHover.ShowWidgets(taskbar, settings));
        Assert.True(SegmentHover.Update(taskbar, measured, cursor, startMenuOpen: false));
        Assert.False(SegmentHover.ShowApps(taskbar, settings));
        Assert.False(SegmentHover.ShowTray(taskbar, settings));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void EachSegmentCanHideIndependently(bool apps, bool tray, bool widgets)
    {
        var settings = new Types.Settings
        {
            IsDynamic = true,
            ShowTray = true, ShowWidgets = true,
            DynamicAppListLayout = new Types.SegmentSettings { ShowOnHover = apps },
            DynamicTrayLayout = new Types.SegmentSettings { ShowOnHover = tray },
            DynamicWidgetsLayout = new Types.SegmentSettings { ShowOnHover = widgets }
        };
        var taskbar = new Types.Taskbar();
        Assert.Equal(!apps, SegmentHover.ShowApps(taskbar, settings));
        Assert.Equal(!tray, SegmentHover.ShowTray(taskbar, settings));
        Assert.Equal(!widgets, SegmentHover.ShowWidgets(taskbar, settings));
        taskbar.AppListHovered = taskbar.TrayHovered = taskbar.WidgetsHovered = true;
        Assert.True(SegmentHover.ShowApps(taskbar, settings));
        Assert.True(SegmentHover.ShowTray(taskbar, settings));
        Assert.True(SegmentHover.ShowWidgets(taskbar, settings));
        var saved = Newtonsoft.Json.JsonConvert.SerializeObject(settings);
        var loaded = Newtonsoft.Json.JsonConvert.DeserializeObject<Types.Settings>(saved);
        Assert.Equal(apps, loaded.DynamicAppListLayout.ShowOnHover);
        Assert.Equal(tray, loaded.DynamicTrayLayout.ShowOnHover);
        Assert.Equal(widgets, loaded.DynamicWidgetsLayout.ShowOnHover);
    }

    [Fact]
    public void HoverRevealsHiddenTrayAndLeavingHidesItWithoutChangingPreferences()
    {
        var taskbar = new Types.Taskbar();
        var measured = new Types.Taskbar { TrayRect = new LocalPInvoke.RECT { Left = 900, Top = 100, Right = 1000, Bottom = 150 } };
        var settings = new Types.Settings { ShowSegmentsOnHover = true, ShowTray = false };
        Assert.True(SegmentHover.Update(taskbar, measured, new LocalPInvoke.POINT { x = 950, y = 120 }));
        Assert.True(SegmentHover.ShowTray(taskbar, settings));
        Assert.False(settings.ShowTray);
        Assert.False(SegmentHover.Update(taskbar, measured, new LocalPInvoke.POINT { x = 950, y = 120 }));
        Assert.True(SegmentHover.Update(taskbar, measured, new LocalPInvoke.POINT { x = 500, y = 120 }));
        Assert.False(SegmentHover.ShowTray(taskbar, settings));
    }

    [Fact]
    public void NegativeAndZeroScreenCoordinatesWorkAndMonitorsRemainIndependent()
    {
        var first = new Types.Taskbar();
        var second = new Types.Taskbar();
        var measured = new Types.Taskbar { TrayRect = new LocalPInvoke.RECT { Left = 0, Top = -100, Right = 100, Bottom = -50 } };
        Assert.True(SegmentHover.Update(first, measured, new LocalPInvoke.POINT { x = 50, y = -75 }));
        Assert.True(first.TrayHovered);
        Assert.False(second.TrayHovered);
        Assert.False(first.WidgetsHovered);
    }
}
