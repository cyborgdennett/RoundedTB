using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace RoundedTB.Tests;

public class SettingsAuditTests
{
    [Fact]
    public void EnablingAnimationDoesNotReplayAnAlreadyVisibleSegment()
    {
        var transition = new HoverTransition();
        var settings = new Types.SegmentSettings();
        transition.Update(false, settings, 0);
        transition.Update(true, settings, 100);
        settings.HoverOpenEffect = Types.HoverAnimationEffect.Slide;
        transition.Update(true, settings, 150);
        Assert.Equal(1, transition.Progress);
    }

    [Fact]
    public void IncreasingAnimationDurationDoesNotMoveTheSegmentBackwards()
    {
        var transition = new HoverTransition();
        var settings = new Types.SegmentSettings { HoverOpenEffect = Types.HoverAnimationEffect.Slide, HoverOpenDurationMs = 200 };
        transition.Update(false, settings, 0);
        transition.Update(true, settings, 100);
        transition.Update(true, settings, 250);
        var progress = transition.Progress;
        settings.HoverOpenDurationMs = 1000;
        transition.Update(true, settings, 300);
        Assert.InRange(transition.Progress, progress, 1);
    }

    [Fact]
    public void ChangingAttentionFromKeepOpenToTimedUpdatesAnExistingFlash()
    {
        var tracker = new AppAttentionReveal();
        var settings = new Types.Settings
        {
            IsDynamic = true, DynamicAppListLayout = new() { ShowOnHover = true },
            RevealOnAppAttention = true, KeepAppAttentionOpenUntilAccepted = true, AppAttentionRevealSeconds = 2
        };
        var monitor = new IntPtr(20);
        tracker.Notify(AppAttentionReveal.Flash, new IntPtr(10), settings, 0);
        Assert.True(tracker.Active(monitor, 3000, _ => true, _ => monitor));
        settings.KeepAppAttentionOpenUntilAccepted = false;
        tracker.Notify(4, IntPtr.Zero, settings, 3000);
        Assert.False(tracker.Active(monitor, 3000, _ => true, _ => monitor));
    }

    [Fact]
    public void DisablingAppOpenRevealClearsItsPendingTimer()
    {
        var tracker = new AppOpenReveal();
        var taskbar = new Types.Taskbar { AppOpenRevealStartedAt = 1000 };
        var settings = new Types.Settings { SimpleTaskbarLayout = new() { ShowOnHover = true }, RevealOnAppOpen = false };
        tracker.Update(new List<Types.Taskbar> { taskbar }, settings);
        settings.RevealOnAppOpen = true;
        Assert.False(AppOpenReveal.Active(taskbar, settings, 1000));
    }

    [AvaloniaFact]
    public void NonFiniteSavedHoverDelayCannotCrashSettingsInitialization()
    {
        var main = new MainWindow();
        try
        {
            var settings = new Types.Settings { IsDynamic = true, DynamicAppListLayout = new() { HoverHideDelaySeconds = double.NaN } };
            main.InitializeUi(settings, true);
            Assert.Equal(0m, main.FindControl<NumericUpDown>("hoverDelayInput").Value);
            Assert.True(double.IsFinite(settings.DynamicAppListLayout.HoverHideDelaySeconds));
        }
        finally { main.shouldReallyDieNoReally = true; main.Close(); }
    }

    [Fact]
    public void ShorteningAppOpenDurationUpdatesAnActiveReveal()
    {
        var taskbar = new Types.Taskbar { AppOpenRevealStartedAt = 1000 };
        var settings = new Types.Settings { SimpleTaskbarLayout = new() { ShowOnHover = true }, RevealOnAppOpen = true };
        Assert.True(AppOpenReveal.Active(taskbar, settings, 1500));
        settings.AppOpenRevealSeconds = 0.2;
        Assert.False(AppOpenReveal.Active(taskbar, settings, 1500));
    }

    [Fact]
    public void IncreasingDurationCannotReplayACompletedAppOpenReveal()
    {
        var taskbar = new Types.Taskbar { AppOpenRevealStartedAt = 1000 };
        var settings = new Types.Settings { SimpleTaskbarLayout = new() { ShowOnHover = true }, RevealOnAppOpen = true };
        AppOpenReveal.Expire(new() { taskbar }, settings, 2000);
        settings.AppOpenRevealSeconds = 60;
        Assert.False(AppOpenReveal.Active(taskbar, settings, 2100));
    }
}
