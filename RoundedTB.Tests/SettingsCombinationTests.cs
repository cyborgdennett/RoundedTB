using System;
using Xunit;

namespace RoundedTB.Tests;

public class SettingsCombinationTests
{
    [Fact]
    public void NormalAndMaximizedVisibilityCombinationsRespectIndependentSegmentsAndShellMenus()
    {
        for (int apps = 0; apps < 2; apps++)
        for (int tray = 0; tray < 3; tray++)
        for (int widgets = 0; widgets < 3; widgets++)
        for (int maxApps = 0; maxApps < 2; maxApps++)
        for (int maxTray = 0; maxTray < 3; maxTray++)
        for (int maxWidgets = 0; maxWidgets < 3; maxWidgets++)
        {
            var settings = new Types.Settings
            {
                IsDynamic = true, ShowTray = tray != 2, ShowWidgets = widgets != 2,
                SimpleTaskbarLayout = new(),
                DynamicAppListLayout = new() { ShowOnHover = apps == 1, MaximizedVisibility = (Types.VisibilityMode)maxApps },
                DynamicTrayLayout = new() { ShowOnHover = tray == 1, MaximizedVisibility = (Types.VisibilityMode)maxTray },
                DynamicWidgetsLayout = new() { ShowOnHover = widgets == 1, MaximizedVisibility = (Types.VisibilityMode)maxWidgets }
            };
            var current = new Types.Taskbar();
            var measured = new Types.Taskbar();
            var cursor = new LocalPInvoke.POINT { x = -100, y = -100 };
            foreach (bool maximized in new[] { false, true, false })
            {
                var effective = WindowModeVisibility.Resolve(settings, maximized);
                int appMode = maximized ? maxApps : apps;
                int trayMode = maximized ? maxTray : tray;
                int widgetMode = maximized ? maxWidgets : widgets;
                void AssertVisibility(bool appShown, bool trayShown, bool widgetsShown)
                {
                    Assert.Equal(appShown, SegmentHover.ShowApps(current, effective));
                    Assert.Equal(trayShown, SegmentHover.ShowTray(current, effective));
                    Assert.Equal(widgetsShown, SegmentHover.ShowWidgets(current, effective));
                }
                SegmentHover.Update(current, measured, cursor, settings: effective);
                AssertVisibility(appMode == 0, trayMode == 0, widgetMode == 0);
                SegmentHover.Update(current, measured, cursor, startMenuOpen: true, settings: effective);
                AssertVisibility(true, true, widgetMode == 0);
                SegmentHover.Update(current, measured, cursor, settings: effective);
                AssertVisibility(appMode == 0, trayMode == 0, widgetMode == 0);
                SegmentHover.Update(current, measured, cursor, windowPreviewOpen: true, settings: effective);
                AssertVisibility(true, trayMode == 0, widgetMode == 0);
                SegmentHover.Update(current, measured, cursor, trayPopupOpen: true, settings: effective);
                AssertVisibility(appMode == 0, trayMode != 2, widgetMode == 0);
                current.AppAttentionRevealActive = true;
                SegmentHover.Update(current, measured, cursor, settings: effective);
                AssertVisibility(true, trayMode == 0, widgetMode == 0);
                current.AppAttentionRevealActive = false;
                current.RevealAllSegments = true;
                SegmentHover.Update(current, measured, cursor, settings: effective);
                AssertVisibility(true, trayMode != 2, widgetMode != 2);
                current.RevealAllSegments = false;
                SegmentHover.Update(current, measured, cursor, settings: effective);
                AssertVisibility(appMode == 0, trayMode == 0, widgetMode == 0);
            }
            Assert.Equal(apps == 1, settings.DynamicAppListLayout.ShowOnHover);
            Assert.Equal(tray != 2, settings.ShowTray);
            Assert.Equal(widgets != 2, settings.ShowWidgets);
        }
    }

    [Fact]
    public void AllAnimationEffectsDurationsAndHideDelaysFinishWithoutOvershooting()
    {
        foreach (Types.HoverAnimationEffect show in Enum.GetValues<Types.HoverAnimationEffect>())
        foreach (Types.HoverAnimationEffect hide in Enum.GetValues<Types.HoverAnimationEffect>())
        foreach (int open in new[] { 0, 1, 200, 5000 })
        foreach (int close in new[] { 0, 1, 200, 5000 })
        foreach (int delay in new[] { 0, 2, 60 })
        {
            var settings = new Types.SegmentSettings
            {
                HoverOpenEffect = show, HoverCloseEffect = hide, HoverOpenDurationMs = open,
                HoverCloseDurationMs = close, HoverHideDelaySeconds = delay
            };
            var transition = new HoverTransition();
            transition.Update(false, settings, 0);
            transition.Update(true, settings, 100);
            double previous = transition.Progress;
            for (long now = 101; now <= 5200; now += 100)
            {
                transition.Update(true, settings, now);
                Assert.InRange(transition.Progress, previous, 1);
                previous = transition.Progress;
            }
            Assert.Equal(1, transition.Progress);
            transition.Update(false, settings, 6000);
            long closing = 6000 + delay * 1000L;
            if (delay > 0)
            {
                transition.Update(false, settings, closing - 1);
                Assert.Equal(1, transition.Progress);
            }
            transition.Update(false, settings, closing);
            previous = transition.Progress;
            for (long now = closing + 1; now <= closing + 5101; now += 100)
            {
                transition.Update(false, settings, now);
                Assert.InRange(transition.Progress, 0, previous);
                previous = transition.Progress;
            }
            Assert.Equal(0, transition.Progress);
        }
    }

    [Theory]
    [InlineData(int.MaxValue, 2.25)]
    [InlineData(int.MinValue, 2.25)]
    [InlineData(int.MaxValue, 5)]
    public void ExtremeSpacingAtHighDpiDoesNotWrapToTheOppositeSide(int value, double scale)
    {
        int pixel = NativeGeometry.Pixel(value * scale);
        Assert.InRange(pixel, -1048576, 1048576);
        Assert.Equal(Math.Sign(value), Math.Sign(pixel));
        var taskbar = new Types.Taskbar
        {
            ScaleFactor = scale,
            TaskbarRect = new() { Left = -3840, Top = -96, Right = 0, Bottom = 0 },
            AppListRect = new() { Left = -2400, Right = -1400 }
        };
        var region = TaskbarIconBounds.GetRegion(taskbar,
            new Types.SegmentSettings { MarginLeft = value, MarginRight = value, MarginTop = value, MarginBottom = value });
        Assert.Equal(Math.Sign(value), Math.Sign(region.Top));
    }
}
