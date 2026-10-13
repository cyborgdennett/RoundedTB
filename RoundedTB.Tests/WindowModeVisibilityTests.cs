using Newtonsoft.Json;
using Xunit;

namespace RoundedTB.Tests;

public class WindowModeVisibilityTests
{
    private static Types.Settings Settings() => new()
    {
        IsDynamic = true, ShowTray = true, ShowWidgets = true,
        SimpleTaskbarLayout = new(), DynamicAppListLayout = new(),
        DynamicTrayLayout = new(), DynamicWidgetsLayout = new()
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OlderSettingsRetainVisibilityWhenMaximized(bool hide)
    {
        var settings = Settings();
        settings.DynamicAppListLayout.ShowOnHover = hide;
        var maximized = WindowModeVisibility.Resolve(settings, true);
        Assert.Equal(hide, maximized.DynamicAppListLayout.ShowOnHover);
        Assert.Same(settings, WindowModeVisibility.Resolve(settings, false));
    }

    [Theory]
    [InlineData(false, Types.VisibilityMode.AutoHide)]
    [InlineData(true, Types.VisibilityMode.AlwaysVisible)]
    public void AppVisibilitySwitchesWithWindowStateWithoutChangingSavedPreferences(bool normalHide, Types.VisibilityMode mode)
    {
        var settings = Settings();
        settings.DynamicAppListLayout.ShowOnHover = normalHide;
        settings.DynamicAppListLayout.MaximizedVisibility = mode;
        var taskbar = new Types.Taskbar();
        var measured = new Types.Taskbar();
        foreach (bool maximized in new[] { false, true, false })
        {
            var effective = WindowModeVisibility.Resolve(settings, maximized);
            SegmentHover.Update(taskbar, measured, default, settings: effective);
            bool expected = !(maximized ? mode == Types.VisibilityMode.AutoHide : normalHide);
            Assert.Equal(expected, SegmentHover.ShowApps(taskbar, effective));
            Assert.Equal(normalHide, settings.DynamicAppListLayout.ShowOnHover);
            Assert.Equal(mode, settings.DynamicAppListLayout.MaximizedVisibility);
        }
    }

    [Theory]
    [InlineData(Types.VisibilityMode.AlwaysVisible, true)]
    [InlineData(Types.VisibilityMode.AutoHide, false)]
    [InlineData(Types.VisibilityMode.Hidden, false)]
    public void TrayAndWidgetsCanOverrideNormalHiddenMode(Types.VisibilityMode mode, bool expected)
    {
        var settings = Settings();
        settings.ShowTray = settings.ShowWidgets = false;
        settings.DynamicTrayLayout.MaximizedVisibility = settings.DynamicWidgetsLayout.MaximizedVisibility = mode;
        var effective = WindowModeVisibility.Resolve(settings, true);
        var taskbar = new Types.Taskbar();
        SegmentHover.Update(taskbar, new(), default, settings: effective);
        Assert.Equal(expected, SegmentHover.ShowTray(taskbar, effective));
        Assert.Equal(expected, SegmentHover.ShowWidgets(taskbar, effective));
        Assert.False(settings.ShowTray);
        Assert.False(settings.ShowWidgets);
        Assert.Same(effective, WindowModeVisibility.Resolve(effective, true));
    }

    [Fact]
    public void MaximizedChoicesSurviveSavingAndKeepHoverGeometry()
    {
        var settings = Settings();
        settings.DynamicAppListLayout.MaximizedVisibility = Types.VisibilityMode.AutoHide;
        settings.DynamicAppListLayout.HoverActivationPill = true;
        settings.DynamicAppListLayout.HoverPillWidth = 123;
        settings.DynamicAppListLayout.MarginLeft = 8;
        var saved = JsonConvert.DeserializeObject<Types.Settings>(JsonConvert.SerializeObject(settings));
        var effective = WindowModeVisibility.Resolve(saved, true);
        Assert.True(effective.DynamicAppListLayout.ShowOnHover);
        Assert.True(effective.DynamicAppListLayout.HoverActivationPill);
        Assert.Equal(123, effective.DynamicAppListLayout.HoverPillWidth);
        Assert.Equal(8, effective.DynamicAppListLayout.MarginLeft);
        Assert.False(saved.DynamicAppListLayout.ShowOnHover);
    }

    [Fact]
    public void AppActivityCanRevealOnlyDuringMaximizedAutoHide()
    {
        var settings = Settings();
        settings.RevealOnAppOpen = settings.RevealOnAppAttention = true;
        settings.DynamicAppListLayout.MaximizedVisibility = Types.VisibilityMode.AutoHide;
        var taskbar = new Types.Taskbar { HasMaximizedWindow = true, AppOpenRevealStartedAt = 1000 };
        Assert.True(AppAttentionReveal.Enabled(settings));
        Assert.True(AppOpenReveal.Active(taskbar, settings, 1000));
        taskbar.HasMaximizedWindow = false;
        Assert.False(AppOpenReveal.Active(taskbar, settings, 1000));
    }
}
