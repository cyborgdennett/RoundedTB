namespace RoundedTB;

internal static class WindowModeVisibility
{
    internal static Types.Settings Resolve(Types.Settings settings, bool maximized)
    {
        if (!maximized || settings.VisibilityResolved) return settings;
        var result = settings.Copy();
        result.VisibilityResolved = true;
        result.SimpleTaskbarLayout = Layout(settings.SimpleTaskbarLayout, true, out _);
        result.DynamicAppListLayout = Layout(settings.DynamicAppListLayout, true, out _);
        result.DynamicTrayLayout = Layout(settings.DynamicTrayLayout, settings.ShowTray, out var tray);
        result.DynamicWidgetsLayout = Layout(settings.DynamicWidgetsLayout, settings.ShowWidgets, out var widgets);
        result.ShowTray = tray;
        result.ShowWidgets = widgets;
        return result;
    }

    private static Types.SegmentSettings Layout(Types.SegmentSettings source, bool shown, out bool visible)
    {
        visible = shown;
        if (source?.MaximizedVisibility is not Types.VisibilityMode mode) return source;
        var result = source.Copy();
        result.ShowOnHover = mode == Types.VisibilityMode.AutoHide;
        visible = mode != Types.VisibilityMode.Hidden;
        return result;
    }

    internal static bool AnyAppAutoHide(Types.Settings settings)
        => SegmentHover.AppSettings(settings)?.ShowOnHover == true
            || SegmentHover.AppSettings(settings)?.MaximizedVisibility == Types.VisibilityMode.AutoHide;
}
