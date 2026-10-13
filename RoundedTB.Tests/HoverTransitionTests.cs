using Xunit;

namespace RoundedTB.Tests;

public class HoverTransitionTests
{
    [Fact]
    public void DelayStartsAtExitAndReenteringRestartsIt()
    {
        var transition = new HoverTransition();
        var settings = new Types.SegmentSettings { HoverHideDelaySeconds = 2 };
        transition.Update(true, settings, 0);
        transition.Update(false, settings, 100);
        transition.Update(false, settings, 2099);
        Assert.Equal(1, transition.Progress);
        transition.Update(true, settings, 2100);
        transition.Update(false, settings, 2200);
        transition.Update(false, settings, 4199);
        Assert.Equal(1, transition.Progress);
        transition.Update(false, settings, 4200);
        Assert.Equal(0, transition.Progress);
    }

    [Fact]
    public void DefaultsHideImmediately()
    {
        var settings = new Types.SegmentSettings();
        var transition = new HoverTransition();
        transition.Update(true, settings, 0);
        transition.Update(false, settings, 1);
        Assert.Equal(0, transition.Progress);
        Assert.Equal(Types.HoverAnimationEffect.None, settings.HoverOpenEffect);
        Assert.Equal(Types.HoverAnimationEffect.None, settings.HoverCloseEffect);
        Assert.Equal(0, settings.HoverHideDelaySeconds);
    }

    [Fact]
    public void SeparateDurationsAndReversalStayContinuousAndWithinBounds()
    {
        var settings = new Types.SegmentSettings { HoverOpenEffect = Types.HoverAnimationEffect.Slide, HoverCloseEffect = Types.HoverAnimationEffect.Slide, HoverOpenDurationMs = 200, HoverCloseDurationMs = 400 };
        var transition = new HoverTransition();
        transition.Update(false, settings, 0);
        transition.Update(true, settings, 100);
        transition.Update(true, settings, 200);
        Assert.Equal(0.5, transition.Progress, 6);
        transition.Update(false, settings, 200);
        Assert.Equal(0.5, transition.Progress, 6);
        transition.Update(false, settings, 400);
        Assert.Equal(0.25, transition.Progress, 6);
        transition.Update(true, settings, 400);
        Assert.Equal(0.25, transition.Progress, 6);
        transition.Update(true, settings, 600);
        Assert.Equal(1, transition.Progress);
        transition.Update(false, settings, 601);
        transition.Update(false, settings, 1001);
        Assert.Equal(0, transition.Progress);
    }

    [Theory]
    [InlineData(Types.HoverAnimationEffect.Slide)]
    [InlineData(Types.HoverAnimationEffect.Expand)]
    public void AnimatedClipCannotExtendBeyondNativeSegment(Types.HoverAnimationEffect effect)
    {
        var settings = new Types.SegmentSettings { HoverOpenDurationMs = 200, HoverOpenEffect = effect };
        var transition = new HoverTransition();
        transition.Update(false, settings, 0);
        transition.Update(true, settings, 1);
        transition.Update(true, settings, 101);
        var original = new LocalPInvoke.RECT { Left = 200, Top = 3, Right = 600, Bottom = 45 };
        var clip = transition.Clip(original, settings);
        Assert.InRange(clip.Left, original.Left, original.Right);
        Assert.InRange(clip.Right, clip.Left, original.Right);
        Assert.InRange(clip.Top, original.Top, original.Bottom);
        Assert.Equal(original.Bottom, clip.Bottom);
        Assert.True(clip.Left > original.Left || clip.Top > original.Top);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoneEffectAppliesIndependentlyToShowingAndHiding(bool instantShow)
    {
        var settings = new Types.SegmentSettings
        {
            HoverOpenEffect = instantShow ? Types.HoverAnimationEffect.None : Types.HoverAnimationEffect.Slide,
            HoverCloseEffect = instantShow ? Types.HoverAnimationEffect.Slide : Types.HoverAnimationEffect.None,
            HoverOpenDurationMs = 200, HoverCloseDurationMs = 200
        };
        var transition = new HoverTransition();
        transition.Update(false, settings, 0);
        transition.Update(true, settings, 100);
        transition.Update(true, settings, 200);
        Assert.Equal(instantShow ? 1 : 0.5, transition.Progress, 6);
        transition.Update(true, settings, 300);
        transition.Update(false, settings, 400);
        transition.Update(false, settings, 500);
        Assert.Equal(instantShow ? 0.5 : 0, transition.Progress, 6);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LegacyAnimationCheckboxMigratesAndIsRemovedOnSave(bool enabled)
    {
        var json = "{\"HoverAnimationEnabled\":" + (enabled ? "true" : "false") + ",\"HoverOpenEffect\":0,\"HoverCloseEffect\":1}";
        var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<Types.SegmentSettings>(json);
        Assert.Equal(enabled ? Types.HoverAnimationEffect.Slide : Types.HoverAnimationEffect.None, settings.HoverOpenEffect);
        Assert.Equal(enabled ? Types.HoverAnimationEffect.Expand : Types.HoverAnimationEffect.None, settings.HoverCloseEffect);
        Assert.DoesNotContain("HoverAnimationEnabled", Newtonsoft.Json.JsonConvert.SerializeObject(settings));
    }

    [Fact]
    public void OpenPreviewAndStartMenuPreventHideCountdown()
    {
        var settings = new Types.Settings { IsDynamic = true, DynamicAppListLayout = new() { ShowOnHover = true, HoverHideDelaySeconds = 1 } };
        var taskbar = new Types.Taskbar();
        var measured = new Types.Taskbar();
        var cursor = new LocalPInvoke.POINT { x = 100, y = 100 };
        SegmentHover.Update(taskbar, measured, cursor, windowPreviewOpen: true, settings: settings, timestamp: 0);
        SegmentHover.Update(taskbar, measured, cursor, windowPreviewOpen: true, settings: settings, timestamp: 5000);
        Assert.True(SegmentHover.ShowApps(taskbar, settings));
        SegmentHover.Update(taskbar, measured, cursor, startMenuOpen: true, settings: settings, timestamp: 6000);
        Assert.True(SegmentHover.ShowApps(taskbar, settings));
        SegmentHover.Update(taskbar, measured, cursor, settings: settings, timestamp: 7000);
        Assert.True(SegmentHover.ShowApps(taskbar, settings));
        SegmentHover.Update(taskbar, measured, cursor, settings: settings, timestamp: 8000);
        Assert.False(SegmentHover.ShowApps(taskbar, settings));
    }
}
