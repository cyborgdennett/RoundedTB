using System;
using Xunit;

namespace RoundedTB.Tests;

public class AppAttentionRevealTests
{
    private static Types.Settings Settings() => new()
    {
        IsDynamic = true, RevealOnAppAttention = true, AppAttentionRevealSeconds = 2,
        DynamicAppListLayout = new() { ShowOnHover = true },
        DynamicTrayLayout = new() { ShowOnHover = true },
        DynamicWidgetsLayout = new() { ShowOnHover = true }
    };

    [Fact]
    public void OnlyFlashRequestsRevealAndTargetsTheAppsMonitor()
    {
        var tracker = new AppAttentionReveal();
        var settings = Settings();
        var window = new IntPtr(10);
        var monitor = new IntPtr(20);
        tracker.Notify(6, window, settings, 1000); // Ordinary redraw is not a notification.
        Assert.False(tracker.Active(monitor, 1000, _ => true, _ => monitor));
        tracker.Notify(AppAttentionReveal.Flash, window, settings, 1000);
        Assert.True(tracker.Active(monitor, 2999, _ => true, _ => monitor));
        Assert.False(tracker.Active(new IntPtr(30), 2999, _ => true, _ => monitor));
        Assert.False(tracker.Active(monitor, 3000, _ => true, _ => monitor));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(0x8004)]
    public void ClosingOrActivatingAppDismissesItsReveal(int shellCode)
    {
        var tracker = new AppAttentionReveal();
        var settings = Settings();
        var window = new IntPtr(10);
        tracker.Notify(AppAttentionReveal.Flash, window, settings, 0);
        tracker.Notify(shellCode, window, settings, 100);
        Assert.False(tracker.Active(new IntPtr(20), 100, _ => true, _ => new IntPtr(20)));
    }

    [Fact]
    public void RepeatedFlashesDoNotExtendRevealAndMissingWindowsAreRemoved()
    {
        var tracker = new AppAttentionReveal();
        var settings = Settings();
        var window = new IntPtr(10);
        var monitor = new IntPtr(20);
        tracker.Notify(AppAttentionReveal.Flash, window, settings, 0);
        tracker.Notify(AppAttentionReveal.Flash, window, settings, 1500);
        Assert.False(tracker.Active(monitor, 3000, _ => true, _ => monitor));
        tracker.Notify(AppAttentionReveal.Flash, window, settings, 3050);
        Assert.False(tracker.Active(monitor, 3050, _ => true, _ => monitor));
        Assert.False(tracker.Active(monitor, 3100, _ => false, _ => monitor));
        Assert.False(tracker.Active(monitor, 3200, _ => true, _ => monitor));
    }

    [Fact]
    public void RequiresOptInAndTaskbarHoverAndRevealsOnlyApps()
    {
        var tracker = new AppAttentionReveal();
        var settings = Settings();
        settings.RevealOnAppAttention = false;
        tracker.Notify(AppAttentionReveal.Flash, new IntPtr(10), settings, 0);
        Assert.False(tracker.Active(new IntPtr(20), 10, _ => true, _ => new IntPtr(20)));
        settings.RevealOnAppAttention = true;
        var taskbar = new Types.Taskbar { AppAttentionRevealActive = true };
        SegmentHover.Update(taskbar, new(), new() { x = -100, y = -100 }, settings: settings, timestamp: 0);
        Assert.True(SegmentHover.ShowApps(taskbar, settings));
        Assert.False(SegmentHover.ShowTray(taskbar, settings));
        Assert.False(SegmentHover.ShowWidgets(taskbar, settings));
        settings.DynamicAppListLayout.ShowOnHover = false;
        Assert.False(AppAttentionReveal.Enabled(settings));
    }
    [Fact]
    public void KeepOpenRemainsUntilActivation()
    {
        var tracker = new AppAttentionReveal();
        var settings = Settings();
        var window = new IntPtr(10);
        var monitor = new IntPtr(20);
        settings.KeepAppAttentionOpenUntilAccepted = true;
        tracker.Notify(AppAttentionReveal.Flash, window, settings, 0);
        Assert.True(tracker.Active(monitor, 60000, _ => true, _ => monitor));
        tracker.Notify(4, window, settings, 60001);
        Assert.False(tracker.Active(monitor, 60002, _ => true, _ => monitor));
    }
}
