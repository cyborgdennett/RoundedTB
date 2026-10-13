using System;
using Xunit;

namespace RoundedTB.Tests;

public class ActivationStripSurfaceTests
{
    [Fact]
    public void SurfacesMatchNativePillsAndDisappearForOpenOrRestoredSegments()
    {
        var settings = new Types.Settings
        {
            IsDynamic = true,
            DynamicAppListLayout = new() { ShowOnHover = true, HoverActivationPill = true },
            DynamicTrayLayout = new() { ShowOnHover = true, HoverActivationPill = true }
        };
        var taskbar = new Types.Taskbar
        {
            TaskbarHwnd = new IntPtr(1), TrayHwnd = new IntPtr(2), ScaleFactor = 1.5,
            TaskbarRect = new() { Left = -1920, Top = -60, Right = 0, Bottom = 0 },
            AppListRect = new() { Left = -1100, Right = -700 },
            TrayRect = new() { Left = -300, Right = 0 }
        };
        var specs = ActivationStripSurfaces.Describe(new[] { taskbar }, settings);
        Assert.Equal(2, specs.Length);
        Assert.Equal(-960, specs[0].Left);
        Assert.Equal(-4, specs[0].Top);
        Assert.Equal(120, specs[0].Width);
        Assert.Equal(4, specs[0].Height);
        Assert.False(specs[0].Tray);
        Assert.True(specs[1].Tray);
        taskbar.AppHoverTransition.Update(true, settings.DynamicAppListLayout, 0);
        Assert.Single(ActivationStripSurfaces.Describe(new[] { taskbar }, settings));
        taskbar.TaskbarHidden = true;
        Assert.Empty(ActivationStripSurfaces.Describe(new[] { taskbar }, settings));
        taskbar.TaskbarHidden = false;
        taskbar.AppearanceRestored = true;
        Assert.Empty(ActivationStripSurfaces.Describe(new[] { taskbar }, settings));
        taskbar.AppearanceRestored = false;
        settings.IsWindowsDefault = true;
        Assert.Empty(ActivationStripSurfaces.Describe(new[] { taskbar }, settings));
    }
}
