using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(RoundedTB.Tests.TestAppBuilder))]

namespace RoundedTB.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public class WindowTests
{
    [AvaloniaFact]
    public void SectionLinksScrollTheSamePageAndStartupLivesInWindowsIntegration()
    {
        var main = new MainWindow();
        typeof(MainWindow).GetField("initializationStarted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(main, true);
        main.InitializeUi(Settings(), true);
        main.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var scroll = main.FindControl<ScrollViewer>("settingsScrollViewer");
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        main.FindControl<Button>("advancedNavigationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(scroll.Offset.Y > 0);
        Assert.True(main.FindControl<Button>("visibilityAlwaysButton").IsEffectivelyVisible);
        Assert.True(main.FindControl<CheckBox>("expandWorkAreaCheckBox").IsEffectivelyVisible);
        Assert.Contains(main.FindControl<CheckBox>("StartupCheckBox"),
            Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(main.FindControl<StackPanel>("windowsSection")));
        main.FindControl<Button>("generalNavigationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, scroll.Offset.Y);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData("trayVisibilityCombo", "trayRectStandIn", false)]
    [InlineData("widgetsVisibilityCombo", "widgetsRectStandIn", true)]
    public void VisibilityChoicesDistinguishHiddenFromAccessibleAutoHide(string comboName, string areaButton, bool widgets)
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.IsDynamic = true;
        settings.IsCentred = true;
        settings.ShowTray = settings.ShowWidgets = false;
        main.InitializeUi(settings, true);
        var combo = main.FindControl<ComboBox>(comboName);
        var area = widgets ? settings.DynamicWidgetsLayout : settings.DynamicTrayLayout;
        Assert.Equal(2, combo.SelectedIndex);
        combo.SelectedIndex = 1;
        Assert.True(area.ShowOnHover);
        Assert.True(widgets ? settings.ShowWidgets : settings.ShowTray);
        combo.SelectedIndex = 2;
        Assert.False(area.ShowOnHover);
        Assert.False(widgets ? settings.ShowWidgets : settings.ShowTray);
        main.FindControl<Button>(areaButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Contains("hidden", main.FindControl<TextBlock>("selectedAreaHint").Text);
        Assert.Equal(widgets ? "Editing: Widgets" : "Editing: System tray", main.FindControl<TextBlock>("hoverEditingLabel").Text);
        Assert.False(main.FindControl<StackPanel>("hoverOptionsPanel").IsEnabled);
        combo.SelectedIndex = 0;
        Assert.False(area.ShowOnHover);
        Assert.True(widgets ? settings.ShowWidgets : settings.ShowTray);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void ContinuousLayoutPreservesSeparateAreaVisibilityPreferences()
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.IsDynamic = true;
        settings.DynamicTrayLayout.ShowOnHover = true;
        settings.ShowWidgets = false;
        main.InitializeUi(settings, true);
        main.FindControl<CheckBox>("dynamicCheckBox").IsChecked = false;
        Assert.False(main.FindControl<ComboBox>("trayVisibilityCombo").IsEnabled);
        Assert.False(main.FindControl<ComboBox>("widgetsVisibilityCombo").IsEnabled);
        Assert.Equal("Entire taskbar", main.FindControl<TextBlock>("appVisibilityLabel").Text);
        main.FindControl<CheckBox>("dynamicCheckBox").IsChecked = true;
        Assert.Equal(1, main.FindControl<ComboBox>("trayVisibilityCombo").SelectedIndex);
        Assert.Equal(2, main.FindControl<ComboBox>("widgetsVisibilityCombo").SelectedIndex);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void ActivitySettingsApplyImmediately()
    {
        var main = new MainWindow();
        var settings = Settings();
        main.InitializeUi(settings, true);
        main.FindControl<ComboBox>("appVisibilityCombo").SelectedIndex = 1;
        Assert.True(settings.SimpleTaskbarLayout.ShowOnHover);
        var duration = main.FindControl<NumericUpDown>("appOpenDurationInput");
        duration.Value = 2;
        Assert.Equal(2, settings.AppOpenRevealSeconds);
        duration.Value = 1;
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData("revealOnAppOpenCheckBox")]
    [InlineData("revealOnAppAttentionCheckBox")]
    [InlineData("keepAppAttentionOpenCheckBox")]
    [InlineData("fillMaximisedCheckBox")]
    [InlineData("fillAltTabCheckBox")]
    [InlineData("expandWorkAreaCheckBox")]
    public void PreviouslyDeferredOptionsApplyImmediately(string id)
    {
        var main = new MainWindow();
        var settings = Settings();
        main.InitializeUi(settings, true);
        var control = main.FindControl<CheckBox>(id);
        control.IsChecked = control.IsChecked != true;
        bool value = id switch
        {
            "revealOnAppOpenCheckBox" => settings.RevealOnAppOpen,
            "revealOnAppAttentionCheckBox" => settings.RevealOnAppAttention,
            "keepAppAttentionOpenCheckBox" => settings.KeepAppAttentionOpenUntilAccepted,
            "fillMaximisedCheckBox" => settings.FillOnMaximise,
            "fillAltTabCheckBox" => settings.FillOnTaskSwitch,
            _ => settings.ExpandMaximizedWorkArea
        };
        Assert.Equal(control.IsChecked == true, value);
        Assert.Null(main.FindControl<Button>("applyButton"));
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void ClosingConfigurationHidesWindowAndKeepsTrayRunning()
    {
        var main = new MainWindow();
        // Exercise window lifetime without starting the Windows worker in headless tests.
        typeof(MainWindow).GetField("initializationStarted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(main, true);
        main.InitializeUi(Settings(), true);
        main.CreateTrayIcon();
        main.Show();
        main.Close();
        Assert.False(main.shouldReallyDieNoReally);
        Assert.False(main.IsVisible);
        Assert.Single(TrayIcon.GetIcons(Application.Current));
        main.Exit();
        Assert.True(main.shouldReallyDieNoReally);
        Assert.Empty(TrayIcon.GetIcons(Application.Current));
    }

    [AvaloniaFact]
    public void HoverTimingAndEffectsPreviewAndPersistPerSegment()
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.IsDynamic = true;
        main.InitializeUi(settings, true);
        var taskbar = new Types.Taskbar();
        main.taskbarDetails.Add(taskbar);
        main.FindControl<Button>("taskbarRectStandIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        main.FindControl<CheckBox>("showSegmentsOnHoverCheckBox").IsChecked = true;
        main.FindControl<CheckBox>("hoverPillCheckBox").IsChecked = true;
        main.FindControl<NumericUpDown>("hoverPillWidthInput").Value = 100;
        main.FindControl<NumericUpDown>("hoverPillThicknessInput").Value = 2;
        main.FindControl<NumericUpDown>("hoverDelayInput").Value = 1.5m;
        main.FindControl<ComboBox>("hoverOpenEffectCombo").SelectedIndex = 1;
        main.FindControl<NumericUpDown>("hoverOpenDurationInput").Value = 250;
        main.FindControl<NumericUpDown>("hoverCloseDurationInput").Value = 400;
        main.FindControl<ComboBox>("hoverCloseEffectCombo").SelectedIndex = 2;
        Assert.True(taskbar.Ignored);
        Assert.Equal(1.5, settings.DynamicAppListLayout.HoverHideDelaySeconds);
        Assert.Equal(Types.HoverAnimationEffect.Slide, settings.DynamicAppListLayout.HoverOpenEffect);
        main.FindControl<Button>("trayRectStandIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0m, main.FindControl<NumericUpDown>("hoverDelayInput").Value);
        Assert.Equal(0, main.FindControl<ComboBox>("hoverOpenEffectCombo").SelectedIndex);
        main.FindControl<Button>("taskbarRectStandIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1.5m, main.FindControl<NumericUpDown>("hoverDelayInput").Value);
        Assert.Equal(400m, main.FindControl<NumericUpDown>("hoverCloseDurationInput").Value);
        var saved = Newtonsoft.Json.JsonConvert.SerializeObject(settings);
        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<Types.Settings>(saved);
        Assert.True(restored.DynamicAppListLayout.HoverActivationPill);
        Assert.Equal(100, restored.DynamicAppListLayout.HoverPillWidth);
        Assert.Equal(2, restored.DynamicAppListLayout.HoverPillThickness);
        Assert.False(restored.DynamicTrayLayout.HoverActivationPill);
        Assert.Equal(250, restored.DynamicAppListLayout.HoverOpenDurationMs);
        Assert.Equal(400, restored.DynamicAppListLayout.HoverCloseDurationMs);
        Assert.Equal(Types.HoverAnimationEffect.Expand, restored.DynamicAppListLayout.HoverCloseEffect);
        Assert.Equal(Types.HoverAnimationEffect.None, restored.DynamicTrayLayout.HoverOpenEffect);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void HoverTogglePreviewsImmediatelyAndRestoresVisibleSegments()
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.IsDynamic = true;
        settings.ShowTray = true;
        settings.ShowWidgets = false;
        main.InitializeUi(settings, true);
        var taskbar = new Types.Taskbar();
        main.taskbarDetails.Add(taskbar);
        main.FindControl<Button>("trayRectStandIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var toggle = main.FindControl<CheckBox>("showSegmentsOnHoverCheckBox");
        toggle.IsChecked = true;
        Assert.True(settings.DynamicTrayLayout.ShowOnHover);
        Assert.False(settings.DynamicAppListLayout.ShowOnHover);
        Assert.True(settings.ShowTray);
        Assert.False(settings.ShowWidgets);
        Assert.True(taskbar.Ignored);
        settings.ShowTray = false; // Older hover builds persisted this while hidden.
        toggle.IsChecked = false;
        Assert.False(settings.DynamicTrayLayout.ShowOnHover);
        Assert.True(settings.ShowTray);
        Assert.False(settings.ShowWidgets);
        Assert.True(main.FindControl<CheckBox>("showTrayCheckBox").IsChecked);
        Assert.True(taskbar.Ignored);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(Avalonia.Input.Key.Up, 1)]
    [InlineData(Avalonia.Input.Key.Right, 1)]
    [InlineData(Avalonia.Input.Key.Down, -1)]
    [InlineData(Avalonia.Input.Key.Left, -1)]
    public void NumericArrowsUpdateSelectedSegmentAndRequestImmediateRedraw(Avalonia.Input.Key key, int step)
    {
        var main = new MainWindow();
        var settings = Settings();
        main.InitializeUi(settings, true);
        var taskbar = new Types.Taskbar();
        main.taskbarDetails.Add(taskbar);
        var input = main.FindControl<NumericUpDown>("mTopInput");
        input.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = key });
        Assert.Equal(3 + step, settings.SimpleTaskbarLayout.MarginTop);
        Assert.Equal(3 + step, input.Value);
        Assert.True(taskbar.Ignored);
        Assert.True(input.ShowButtonSpinner);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void TypingAndSpinnerValuesPreviewImmediatelyAndKeepRadiusSliderInSync()
    {
        var main = new MainWindow();
        var settings = Settings();
        main.InitializeUi(settings, true);
        var taskbar = new Types.Taskbar();
        main.taskbarDetails.Add(taskbar);
        main.FindControl<NumericUpDown>("mLeftInput").Text = "-4";
        Assert.Equal(-4, settings.SimpleTaskbarLayout.MarginLeft);
        Assert.True(taskbar.Ignored);
        taskbar.Ignored = false;
        main.FindControl<NumericUpDown>("cornerRadiusInput").Value = 12;
        Assert.Equal(12, settings.SimpleTaskbarLayout.CornerRadius);
        Assert.Null(main.FindControl<Slider>("cornerRadiusSlider"));
        Assert.True(taskbar.Ignored);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void AllWindowsLoadWithTheirResourcesWithoutStartingTaskbarWorker()
    {
        var main = new MainWindow();
        var about = new AboutWindow();
        var info = new Infobox();
        var effect = new TaskbarEffect();

        Assert.NotNull(main.Icon);
        Assert.False(main.taskbarThread.IsBusy);
        Assert.False(main.IsVisible);
        Assert.NotNull(about.FindControl<Image>("bannerCan").Source);
        Assert.NotNull(info.FindControl<TextBlock>("bodyBlock"));
        Assert.NotNull(effect.FindControl<Avalonia.Controls.Shapes.Ellipse>("eye"));
        Assert.False(effect.IsVisible);
        main.shouldReallyDieNoReally = true;
        main.Close();
        about.Close();
        info.Close();
        effect.Close();
    }

    [AvaloniaFact]
    public void SelectingSegmentsLoadsTheirIndependentSettings()
    {
        var main = new MainWindow();
        main.activeSettings = new Types.Settings
        {
            SimpleTaskbarLayout = new Types.SegmentSettings { CornerRadius = 7, MarginTop = 3 },
            DynamicAppListLayout = new Types.SegmentSettings { CornerRadius = 10 },
            DynamicTrayLayout = new Types.SegmentSettings { CornerRadius = 12, MarginTop = 5 },
            DynamicWidgetsLayout = new Types.SegmentSettings { CornerRadius = 14, MarginTop = -2 }
        };

        main.FindControl<Button>("trayRectStandIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, main.selectedSegment);
        Assert.Equal("12", main.FindControl<NumericUpDown>("cornerRadiusInput").Text);
        Assert.Equal("5", main.FindControl<NumericUpDown>("mTopInput").Text);
        Assert.True(main.FindControl<CheckBox>("showTrayCheckBox").IsVisible);

        main.FindControl<Button>("widgetsRectStandIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(3, main.selectedSegment);
        Assert.Equal("14", main.FindControl<NumericUpDown>("cornerRadiusInput").Text);
        Assert.Equal("-2", main.FindControl<NumericUpDown>("mTopInput").Text);

        main.FindControl<Button>("taskbarRectStandIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0, main.selectedSegment);
        Assert.Equal("7", main.FindControl<NumericUpDown>("cornerRadiusInput").Text);
        main.activeSettings.IsDynamic = true;
        main.FindControl<Button>("taskbarRectStandIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, main.selectedSegment);
        Assert.Equal("10", main.FindControl<NumericUpDown>("cornerRadiusInput").Text);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void EditingMarginsWritesOnlyTheSelectedSegment()
    {
        var main = new MainWindow();
        main.activeSettings.SimpleTaskbarLayout = new Types.SegmentSettings { MarginTop = 3 };
        main.activeSettings.DynamicTrayLayout = new Types.SegmentSettings { MarginTop = 5 };
        main.FindControl<Button>("trayRectStandIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var input = main.FindControl<NumericUpDown>("mTopInput");
        input.Text = "-4";
        input.RaiseEvent(new RoutedEventArgs(Avalonia.Input.InputElement.LostFocusEvent));
        Assert.Equal(-4, main.activeSettings.DynamicTrayLayout.MarginTop);
        Assert.Equal(3, main.activeSettings.SimpleTaskbarLayout.MarginTop);
        input.Text = "invalid";
        input.RaiseEvent(new RoutedEventArgs(Avalonia.Input.InputElement.LostFocusEvent));
        Assert.Equal(-4, main.activeSettings.DynamicTrayLayout.MarginTop);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    private static Types.Settings Settings() => new Types.Settings
    {
        SimpleTaskbarLayout = new Types.SegmentSettings { CornerRadius = 7, MarginTop = 3 },
        DynamicAppListLayout = new Types.SegmentSettings { CornerRadius = 10, MarginTop = 4 },
        DynamicTrayLayout = new Types.SegmentSettings { CornerRadius = 70, MarginTop = 5 },
        DynamicWidgetsLayout = new Types.SegmentSettings { CornerRadius = 14, MarginTop = -2 }
    };

    [AvaloniaFact]
    public void LoadingInitializedEditorDoesNotClampPersistedCornerRadius()
    {
        var main = new MainWindow();
        var settings = Settings();
        main.InitializeUi(settings, true);
        main.FindControl<Button>("trayRectStandIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(70, settings.DynamicTrayLayout.CornerRadius);
        Assert.Equal("70", main.FindControl<NumericUpDown>("cornerRadiusInput").Text);
        main.FindControl<NumericUpDown>("cornerRadiusInput").Value = 22;
        Assert.Equal(22, settings.DynamicTrayLayout.CornerRadius);
        Assert.Equal(7, settings.SimpleTaskbarLayout.CornerRadius);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void ModeSwitchSelectsCorrectEditorWithoutLosingPendingValues()
    {
        var main = new MainWindow();
        var settings = Settings();
        main.InitializeUi(settings, true);
        main.FindControl<NumericUpDown>("mTopInput").Text = "8";
        main.FindControl<CheckBox>("dynamicCheckBox").IsChecked = true;
        Assert.Equal(8, settings.SimpleTaskbarLayout.MarginTop);
        Assert.Equal(1, main.selectedSegment);
        Assert.Equal("10", main.FindControl<NumericUpDown>("cornerRadiusInput").Text);
        main.FindControl<NumericUpDown>("mTopInput").Text = "11";
        main.FindControl<CheckBox>("dynamicCheckBox").IsChecked = false;
        Assert.Equal(11, settings.DynamicAppListLayout.MarginTop);
        Assert.Equal(0, main.selectedSegment);
        Assert.Equal("8", main.FindControl<NumericUpDown>("mTopInput").Text);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void AppActivityBelongsToTaskbarAndItsTestLivesInAdvanced()
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.IsDynamic = true;
        main.InitializeUi(settings, true);
        var activity = main.FindControl<StackPanel>("notificationsSection");
        Assert.True(activity.IsVisible);
        var advanced = main.FindControl<StackPanel>("advancedSection");
        Assert.Contains(main.FindControl<Button>("testAppAttentionButton"),
            Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(advanced));
        Assert.Contains(main.FindControl<ComboBox>("appAttentionTestStyleComboBox"),
            Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(advanced));
        main.FindControl<Button>("widgetsRectStandIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(activity.IsVisible);
        main.FindControl<Button>("trayRectStandIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(activity.IsVisible);
        main.FindControl<Button>("notificationsNavigationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(activity.IsVisible);
        Assert.Equal(1, main.selectedSegment);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void NoneEffectsDisableOnlyTheirOwnDurationAndNeedNoAnimationCheckbox()
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.SimpleTaskbarLayout.ShowOnHover = true;
        main.InitializeUi(settings, true);
        Assert.Null(main.FindControl<CheckBox>("hoverAnimationCheckBox"));
        var show = main.FindControl<ComboBox>("hoverOpenEffectCombo");
        var hide = main.FindControl<ComboBox>("hoverCloseEffectCombo");
        Assert.False(main.FindControl<NumericUpDown>("hoverOpenDurationInput").IsEnabled);
        Assert.False(main.FindControl<NumericUpDown>("hoverCloseDurationInput").IsEnabled);
        show.SelectedIndex = 1;
        Assert.True(main.FindControl<NumericUpDown>("hoverOpenDurationInput").IsEnabled);
        Assert.False(main.FindControl<NumericUpDown>("hoverCloseDurationInput").IsEnabled);
        hide.SelectedIndex = 2;
        show.SelectedIndex = 0;
        Assert.False(main.FindControl<NumericUpDown>("hoverOpenDurationInput").IsEnabled);
        Assert.True(main.FindControl<NumericUpDown>("hoverCloseDurationInput").IsEnabled);
        Assert.Equal(Types.HoverAnimationEffect.None, settings.SimpleTaskbarLayout.HoverOpenEffect);
        Assert.Equal(Types.HoverAnimationEffect.Expand, settings.SimpleTaskbarLayout.HoverCloseEffect);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void NormalAndMaximizedVisibilityButtonsAreIndependentForEachArea()
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.IsDynamic = true;
        settings.ShowTray = true;
        main.InitializeUi(settings, true);
        main.FindControl<Button>("maximizedVisibilityHoverButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(Types.VisibilityMode.AutoHide, settings.DynamicAppListLayout.MaximizedVisibility);
        Assert.False(settings.DynamicAppListLayout.ShowOnHover);
        Assert.Contains("selected", main.FindControl<Button>("visibilityAlwaysButton").Classes);
        Assert.Contains("selected", main.FindControl<Button>("maximizedVisibilityHoverButton").Classes);
        Assert.True(main.FindControl<StackPanel>("hoverOptionsPanel").IsEnabled);
        main.FindControl<Button>("trayRectStandIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        main.FindControl<Button>("maximizedVisibilityHiddenButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(Types.VisibilityMode.Hidden, settings.DynamicTrayLayout.MaximizedVisibility);
        Assert.True(settings.ShowTray);
        main.FindControl<Button>("taskbarRectStandIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(main.FindControl<Button>("maximizedVisibilityHiddenButton").IsVisible);
        Assert.Contains("selected", main.FindControl<Button>("maximizedVisibilityHoverButton").Classes);
        Assert.Equal(Types.VisibilityMode.Hidden, settings.DynamicTrayLayout.MaximizedVisibility);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaximizedRestorationIsUnavailableWithWorkAreaExpansion(bool savedConflict)
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.FillOnMaximise = true;
        settings.ExpandMaximizedWorkArea = savedConflict;
        main.InitializeUi(settings, true);
        var restore = main.FindControl<CheckBox>("fillMaximisedCheckBox");
        var expand = main.FindControl<CheckBox>("expandWorkAreaCheckBox");
        expand.IsChecked = true;
        Assert.False(restore.IsChecked);
        Assert.False(restore.IsEnabled);
        Assert.False(settings.FillOnMaximise);
        expand.IsChecked = false;
        Assert.True(restore.IsEnabled);
        Assert.False(restore.IsChecked);
        restore.IsChecked = true;
        Assert.True(settings.FillOnMaximise);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void StartupAndAreaChangesKeepVisibilityGridValidDuringLayout()
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.IsDynamic = settings.IsCentred = true;
        main.InitializeUi(settings, true);
        typeof(MainWindow).GetField("initializationStarted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(main, true);
        main.Show();
        var grid = main.FindControl<Grid>("visibilityButtonsGrid");
        var definitions = grid.ColumnDefinitions;
        foreach (var id in new[] { "taskbarRectStandIn", "trayRectStandIn", "widgetsRectStandIn", "taskbarRectStandIn", "trayRectStandIn", "taskbarRectStandIn" })
        {
            main.FindControl<Button>(id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            grid.Measure(new Size(480, 100));
            grid.Arrange(new Rect(0, 0, 480, 100));
            Assert.Same(definitions, grid.ColumnDefinitions);
            Assert.Equal(3, definitions.Count);
            Assert.Equal(id == "taskbarRectStandIn" ? 0 : 96, definitions[2].Width.Value);
        }
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void VisibilityButtonsApplyInOneClickAndHighlightOnlyOneChoice()
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.IsDynamic = true;
        main.InitializeUi(settings, true);
        Assert.False(main.FindControl<Button>("visibilityHiddenButton").IsVisible);
        main.FindControl<Button>("visibilityHoverButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(settings.DynamicAppListLayout.ShowOnHover);
        Assert.Contains("selected", main.FindControl<Button>("visibilityHoverButton").Classes);
        Assert.DoesNotContain("selected", main.FindControl<Button>("visibilityAlwaysButton").Classes);
        main.FindControl<Button>("trayRectStandIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(main.FindControl<Button>("visibilityHiddenButton").IsVisible);
        main.FindControl<Button>("visibilityHiddenButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(settings.ShowTray);
        Assert.False(settings.DynamicTrayLayout.ShowOnHover);
        Assert.Contains("selected", main.FindControl<Button>("visibilityHiddenButton").Classes);
        main.FindControl<Button>("visibilityAlwaysButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(settings.ShowTray);
        Assert.DoesNotContain("selected", main.FindControl<Button>("visibilityHiddenButton").Classes);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task PreviewShowsReclaimedSpaceOnlyWhenExperimentIsActive()
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.IsDynamic = true;
        main.InitializeUi(settings, true);
        var space = main.FindControl<Border>("previewReclaimedSpace");
        var experiment = main.FindControl<CheckBox>("expandWorkAreaCheckBox");
        Assert.False(space.IsVisible);
        experiment.IsChecked = true;
        Assert.True(space.IsVisible);
        experiment.IsChecked = false;
        Assert.False(space.IsVisible);
        experiment.IsChecked = true;
        await main.RestoreWindowsAsync();
        Assert.False(space.IsVisible);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void MonitorPreviewSelectsEachTaskbarArea()
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.IsDynamic = settings.IsCentred = true;
        main.InitializeUi(settings, true);
        Assert.NotNull(main.FindControl<Border>("monitorPreview"));
        Assert.True(main.FindControl<CheckBox>("enableRoundedTaskbarCheckBox").IsChecked);
        foreach (var (id, segment) in new[] { ("widgetsRectStandIn", 3), ("taskbarRectStandIn", 1), ("trayRectStandIn", 2) })
        {
            var button = main.FindControl<Button>(id);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(segment, main.selectedSegment);
            Assert.Equal(segment == 1, main.FindControl<ComboBox>("appVisibilityCombo").IsVisible);
            Assert.Equal(segment == 2, main.FindControl<ComboBox>("trayVisibilityCombo").IsVisible);
            Assert.Equal(segment == 3, main.FindControl<ComboBox>("widgetsVisibilityCombo").IsVisible);
            Assert.Contains("selected", button.Classes);
            Assert.Equal(segment == 3 ? 14 : segment == 1 ? 10 : 70,
                main.FindControl<NumericUpDown>("cornerRadiusInput").Value);
        }
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void ShapeCardSitsBesideVisibilityAndStacksInNarrowWindows()
    {
        var main = new MainWindow();
        main.InitializeUi(Settings(), true);
        typeof(MainWindow).GetField("initializationStarted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(main, true);
        main.Show();
        var shape = main.FindControl<Border>("shapeSpacingCard");
        main.Width = 1200;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, Grid.GetColumn(shape));
        Assert.Equal(0, Grid.GetRow(shape));
        Assert.Null(main.FindControl<TextBlock>("shapeEditingLabel"));
        main.Width = 680;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(main.FindControl<StackPanel>("settingsNavigation").IsVisible);
        Assert.Equal(1, Grid.GetColumn(shape));
        Assert.Equal(0, Grid.GetRow(shape));
        main.Width = 620;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, Grid.GetColumn(shape));
        Assert.Equal(1, Grid.GetRow(shape));
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void NarrowWindowHidesNavigationAndReturnsItsSpaceToSettings()
    {
        var main = new MainWindow();
        main.InitializeUi(Settings(), true);
        typeof(MainWindow).GetField("initializationStarted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(main, true);
        main.Show();
        main.Width = 720;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var navigation = main.FindControl<StackPanel>("settingsNavigation");
        var grid = main.FindControl<Grid>("settingsContentGrid");
        Assert.False(navigation.IsVisible);
        Assert.Equal(0, grid.ColumnDefinitions[0].Width.Value);
        Assert.Equal(0, grid.ColumnSpacing);
        Assert.True(main.FindControl<ScrollViewer>("settingsScrollViewer").IsEffectivelyVisible);
        Assert.True(main.FindControl<Button>("restoreWindowsButton").IsEffectivelyVisible);
        main.Width = 1200;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(navigation.IsVisible);
        Assert.Equal(165, grid.ColumnDefinitions[0].Width.Value);
        Assert.Equal(20, grid.ColumnSpacing);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SegmentPickerAndFooterFitInsideSettings(bool dynamic, bool centred)
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.IsDynamic = dynamic;
        settings.IsCentred = centred;
        main.InitializeUi(settings, true);
        typeof(MainWindow).GetField("initializationStarted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(main, true);
        main.Show();
        main.FindControl<Button>("appearanceNavigationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var root = (Control)main.Content;
        root.Measure(new Size(780, 610));
        root.Arrange(new Rect(0, 0, 780, 610));
        var button = main.FindControl<Button>("taskbarRectStandIn");
        var container = (Control)button.Parent;
        Assert.True(button.Bounds.Width >= 80);
        Assert.True(button.Bounds.Height >= 32);
        Assert.True(button.Bounds.Bottom <= container.Bounds.Height);
        Assert.True(button.Bounds.Right <= container.Bounds.Width);
        Assert.True(main.FindControl<Button>("restoreWindowsButton").Bounds.Width >= 150);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(false, 16, 2)]
    [InlineData(true, 7, 3)]
    public void MissingSegmentsInitializeWithPlatformDefaults(bool windows11, int radius, int margin)
    {
        var main = new MainWindow();
        var settings = new Types.Settings();
        main.InitializeUi(settings, windows11);
        Assert.Equal(radius, settings.SimpleTaskbarLayout.CornerRadius);
        Assert.Equal(margin, settings.DynamicTrayLayout.MarginTop);
        Assert.NotSame(settings.SimpleTaskbarLayout, settings.DynamicTrayLayout);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void TrayIconRegistersWithApplicationAndIsRemovedOnExit()
    {
        var main = new MainWindow();
        main.CreateTrayIcon();
        var icons = TrayIcon.GetIcons(Application.Current);
        var icon = Assert.Single(icons);
        Assert.True(icon.IsVisible);
        Assert.Equal("RoundedTB", icon.ToolTipText);
        Assert.Equal(5, icon.Menu.Items.Count);
        var enable = Assert.IsType<NativeMenuItem>(icon.Menu.Items[0]);
        Assert.Equal("Enable RoundedTB", enable.Header);
        Assert.Equal(MenuItemToggleType.CheckBox, enable.ToggleType);
        Assert.IsType<NativeMenuItemSeparator>(icon.Menu.Items[1]);
        main.Exit();
        Assert.Empty(icons);
        Assert.True(main.shouldReallyDieNoReally);
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task RestoreWindowsPreservesCustomizationAndStopsEffects()
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.IsDynamic = true;
        main.InitializeUi(settings, true);
        await main.RestoreWindowsAsync();
        Assert.True(settings.IsWindowsDefault);
        Assert.False(main.taskbarThread.IsBusy);
        Assert.True(settings.IsDynamic);
        Assert.Equal(10, settings.DynamicAppListLayout.CornerRadius);
        Assert.True(main.FindControl<Button>("restoreWindowsButton").IsEnabled);
        Assert.Equal("Enable RoundedTB", main.FindControl<Button>("restoreWindowsButton").Content);
        Assert.False(main.FindControl<CheckBox>("enableRoundedTaskbarCheckBox").IsChecked);
        Assert.False(main.FindControl<Button>("widgetsRectStandIn").IsVisible);
        Assert.False(main.FindControl<Button>("trayRectStandIn").IsVisible);
        Assert.Equal(3, Grid.GetColumnSpan(main.FindControl<Button>("taskbarRectStandIn")));
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void TaskSwitchRestorationIsIndependentOfMaximizedWindowRestoration()
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.FillOnMaximise = false;
        settings.FillOnTaskSwitch = true;
        main.InitializeUi(settings, true);
        var option = main.FindControl<CheckBox>("fillAltTabCheckBox");
        Assert.True(option.IsEnabled);
        Assert.True(option.IsChecked);
        var maximized = main.FindControl<CheckBox>("fillMaximisedCheckBox");
        maximized.IsChecked = true;
        maximized.IsChecked = false;
        Assert.True(option.IsEnabled);
        Assert.True(option.IsChecked);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void VisibilityOptionsExplainDependenciesAndExperimentalRestoration()
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.FillOnMaximise = true;
        settings.RevealOnAppOpen = true;
        settings.AppOpenRevealSeconds = 2.5;
        main.InitializeUi(settings, true);
        var reveal = main.FindControl<CheckBox>("revealOnAppOpenCheckBox");
        var duration = main.FindControl<StackPanel>("appOpenDurationPanel");
        Assert.False(reveal.IsEnabled);
        Assert.False(duration.IsEnabled);
        Assert.Equal(2.5m, main.FindControl<NumericUpDown>("appOpenDurationInput").Value);
        main.FindControl<CheckBox>("autoHideTaskbarCheckBox").IsChecked = true;
        Assert.True(reveal.IsEnabled);
        Assert.True(duration.IsEnabled);
        reveal.IsChecked = false;
        Assert.False(duration.IsEnabled);
        var maximize = main.FindControl<CheckBox>("fillMaximisedCheckBox");
        var expand = main.FindControl<CheckBox>("expandWorkAreaCheckBox");
        expand.IsChecked = true;
        Assert.False(maximize.IsChecked);
        Assert.False(maximize.IsEnabled);
        Assert.Equal("Show enabled areas, keeping their shape.", main.FindControl<TextBlock>("restorationHeading").Text);
        expand.IsChecked = false;
        Assert.True(maximize.IsEnabled);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void SwitchingLayoutPreservesSegmentHoverPreferences()
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.IsDynamic = true;
        settings.DynamicAppListLayout.ShowOnHover = true;
        settings.DynamicTrayLayout.ShowOnHover = true;
        settings.ShowTray = true;
        main.InitializeUi(settings, true);
        var mode = main.FindControl<CheckBox>("dynamicCheckBox");
        mode.IsChecked = false;
        mode.IsChecked = true;
        Assert.True(settings.DynamicAppListLayout.ShowOnHover);
        Assert.True(settings.DynamicTrayLayout.ShowOnHover);
        Assert.True(main.FindControl<CheckBox>("showSegmentsOnHoverCheckBox").IsChecked);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void GeneralAutoHideSynchronizesWithAppearanceAndPreviewsIndependently()
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.IsDynamic = true;
        main.InitializeUi(settings, true);
        var taskbar = new Types.Taskbar();
        main.taskbarDetails.Add(taskbar);
        var apps = main.FindControl<CheckBox>("autoHideTaskbarCheckBox");
        var tray = main.FindControl<CheckBox>("autoHideTrayCheckBox");
        apps.IsChecked = true;
        Assert.True(settings.DynamicAppListLayout.ShowOnHover);
        Assert.True(main.FindControl<CheckBox>("showSegmentsOnHoverCheckBox").IsChecked);
        Assert.True(taskbar.Ignored);
        tray.IsChecked = true;
        Assert.True(settings.DynamicTrayLayout.ShowOnHover);
        Assert.False(settings.DynamicWidgetsLayout.ShowOnHover);
        main.FindControl<Button>("trayRectStandIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        main.FindControl<CheckBox>("showSegmentsOnHoverCheckBox").IsChecked = false;
        Assert.False(tray.IsChecked);
        Assert.True(apps.IsChecked);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void LegacyGlobalAutoHideMigratesVisibleSegmentsWithoutEnablingHiddenWidgets()
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.IsDynamic = true;
        settings.AutoHide = 1;
        settings.ShowTray = true;
        settings.ShowWidgets = false;
        main.InitializeUi(settings, true);
        Assert.True(settings.DynamicAppListLayout.ShowOnHover);
        Assert.True(settings.DynamicTrayLayout.ShowOnHover);
        Assert.False(settings.DynamicWidgetsLayout.ShowOnHover);
        Assert.Equal(0, settings.AutoHide);
        Assert.True(main.FindControl<CheckBox>("autoHideTaskbarCheckBox").IsChecked);
        Assert.True(main.FindControl<CheckBox>("autoHideTrayCheckBox").IsChecked);
        Assert.DoesNotContain("\"AutoHide\"", Newtonsoft.Json.JsonConvert.SerializeObject(settings));
        settings.DynamicTrayLayout.ShowOnHover = false;
        SegmentHover.MigrateLegacyAutoHide(settings);
        Assert.False(settings.DynamicTrayLayout.ShowOnHover);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

    [AvaloniaFact]
    public void SimpleLayoutAllowsTaskbarAutoHideAndDisablesIndependentTrayAndWidgets()
    {
        var main = new MainWindow();
        var settings = Settings();
        settings.AutoHide = 1;
        main.InitializeUi(settings, true);
        Assert.True(settings.SimpleTaskbarLayout.ShowOnHover);
        Assert.True(main.FindControl<CheckBox>("autoHideTaskbarCheckBox").IsEnabled);
        Assert.False(main.FindControl<CheckBox>("autoHideTrayCheckBox").IsEnabled);
        Assert.False(main.FindControl<CheckBox>("autoHideWidgetsCheckBox").IsEnabled);
        main.FindControl<CheckBox>("showSegmentsOnHoverCheckBox").IsChecked = false;
        Assert.False(main.FindControl<CheckBox>("autoHideTaskbarCheckBox").IsChecked);
        main.shouldReallyDieNoReally = true;
        main.Close();
    }

}
