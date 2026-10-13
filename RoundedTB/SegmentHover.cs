namespace RoundedTB;

internal static class SegmentHover
{
    internal static Types.SegmentSettings AppSettings(Types.Settings settings)
        => settings.IsDynamic ? settings.DynamicAppListLayout : settings.SimpleTaskbarLayout;

    internal static void MigrateLegacyAutoHide(Types.Settings settings)
    {
        if (settings.AutoHide > 0)
        {
            AppSettings(settings).ShowOnHover = true;
            if (settings.IsDynamic)
            {
                if (settings.ShowTray) settings.DynamicTrayLayout.ShowOnHover = true;
                if (settings.ShowWidgets) settings.DynamicWidgetsLayout.ShowOnHover = true;
            }
        }
        settings.AutoHide = 0;
    }

    public static bool Enabled(Types.Settings settings)
        => AppSettings(settings)?.ShowOnHover == true || (settings.IsDynamic && (settings.ShowSegmentsOnHover
            || settings.DynamicTrayLayout?.ShowOnHover == true || settings.DynamicWidgetsLayout?.ShowOnHover == true));

    public static bool ShowApps(Types.Taskbar taskbar, Types.Settings settings)
        => taskbar.RevealAllSegments || taskbar.AppAttentionRevealActive || AppSettings(settings)?.ShowOnHover != true || (taskbar.AppHoverTransition.Initialized ? taskbar.AppHoverTransition.Progress > 0 : taskbar.AppListHovered || taskbar.StartMenuOpen || taskbar.WindowPreviewOpen);

    public static bool ShowTray(Types.Taskbar taskbar, Types.Settings settings)
        => taskbar.StartMenuOpen || (taskbar.RevealAllSegments ? settings.ShowTray || settings.ShowSegmentsOnHover || settings.DynamicTrayLayout?.ShowOnHover == true : (settings.ShowSegmentsOnHover || settings.DynamicTrayLayout?.ShowOnHover == true) ? (taskbar.TrayHoverTransition.Initialized ? taskbar.TrayHoverTransition.Progress > 0 : taskbar.TrayHovered || taskbar.TrayPopupOpen) : settings.ShowTray);

    public static bool ShowWidgets(Types.Taskbar taskbar, Types.Settings settings)
        => taskbar.RevealAllSegments ? settings.ShowWidgets || settings.ShowSegmentsOnHover || settings.DynamicWidgetsLayout?.ShowOnHover == true : (settings.ShowSegmentsOnHover || settings.DynamicWidgetsLayout?.ShowOnHover == true) ? (taskbar.WidgetsHoverTransition.Initialized ? taskbar.WidgetsHoverTransition.Progress > 0 : taskbar.WidgetsHovered) : settings.ShowWidgets;

    public static bool Update(Types.Taskbar current, Types.Taskbar measured, LocalPInvoke.POINT cursor, bool startMenuOpen = false, bool windowPreviewOpen = false, Types.Settings settings = null, long? timestamp = null, bool trayPopupOpen = false)
    {
        var apps = settings?.IsDynamic == false ? measured.TaskbarRect : measured.AppListRect;
        // Keep the native full height as a target even while its region is hidden.
        apps.Top = measured.TaskbarRect.Top;
        apps.Bottom = measured.TaskbarRect.Bottom;
        var appHovered = ActivationContains(apps, measured, settings == null ? null : AppSettings(settings), current.AppListHovered || current.AppHoverTransition.Progress > 0, cursor);
        var tray = ActivationContains(measured.TrayRect, measured, settings?.DynamicTrayLayout, current.TrayHovered || current.TrayHoverTransition.Progress > 0, cursor);
        var widgets = measured.HasWidgets && Contains(measured.WidgetsRect, cursor);
        var changed = trayPopupOpen != current.TrayPopupOpen || windowPreviewOpen != current.WindowPreviewOpen || startMenuOpen != current.StartMenuOpen || appHovered != current.AppListHovered || tray != current.TrayHovered || widgets != current.WidgetsHovered;
        current.TrayPopupOpen = trayPopupOpen;
        current.WindowPreviewOpen = windowPreviewOpen;
        current.StartMenuOpen = startMenuOpen;
        current.AppListHovered = appHovered;
        current.TrayHovered = tray;
        current.WidgetsHovered = widgets;
        if (settings != null)
        {
            var now = timestamp ?? System.Environment.TickCount64;
            changed |= current.AppHoverTransition.Update(current.RevealAllSegments || current.AppAttentionRevealActive || AppSettings(settings)?.ShowOnHover != true || appHovered || startMenuOpen || windowPreviewOpen || (!settings.IsDynamic && trayPopupOpen), current.RevealAllSegments || startMenuOpen ? null : AppSettings(settings), now);
            changed |= current.TrayHoverTransition.Update(startMenuOpen || ((settings.ShowSegmentsOnHover || settings.DynamicTrayLayout?.ShowOnHover == true) ? tray || trayPopupOpen || current.RevealAllSegments : settings.ShowTray), current.RevealAllSegments || startMenuOpen ? null : settings.DynamicTrayLayout, now);
            changed |= current.WidgetsHoverTransition.Update((settings.ShowSegmentsOnHover || settings.DynamicWidgetsLayout?.ShowOnHover == true) ? widgets || current.RevealAllSegments : settings.ShowWidgets, current.RevealAllSegments ? null : settings.DynamicWidgetsLayout, now);
        }
        return changed;
    }

    internal static LocalPInvoke.RECT ActivationPill(LocalPInvoke.RECT segment, Types.Taskbar taskbar, Types.SegmentSettings settings)
    {
        if (segment.Right <= segment.Left || taskbar.TaskbarRect.Bottom <= taskbar.TaskbarRect.Top) return default;
        var scale = taskbar.ScaleFactor > 0 ? taskbar.ScaleFactor : 1;
        var width = System.Math.Min(segment.Right - segment.Left, (int)System.Math.Round(System.Math.Clamp(settings.HoverPillWidth, 16, 600) * scale));
        var thickness = System.Math.Min(taskbar.TaskbarRect.Bottom - taskbar.TaskbarRect.Top,
            System.Math.Max(1, (int)System.Math.Round(System.Math.Clamp(settings.HoverPillThickness, 1, 12) * scale)));
        var left = segment.Left + (segment.Right - segment.Left - width) / 2;
        return new LocalPInvoke.RECT { Left = left, Right = left + width,
            Top = taskbar.TaskbarRect.Bottom - thickness, Bottom = taskbar.TaskbarRect.Bottom };
    }

    private static bool ActivationContains(LocalPInvoke.RECT segment, Types.Taskbar measured,
        Types.SegmentSettings settings, bool open, LocalPInvoke.POINT cursor)
    {
        if (settings?.ShowOnHover != true || !settings.HoverActivationPill) return Contains(segment, cursor);
        return Contains(ActivationPill(segment, measured, settings), cursor) || (open && Contains(segment, cursor));
    }

    private static bool Contains(LocalPInvoke.RECT rect, LocalPInvoke.POINT point)
        => rect.Right > rect.Left && rect.Bottom > rect.Top
            && point.x >= rect.Left && point.x < rect.Right
            && point.y >= rect.Top && point.y < rect.Bottom;
}
