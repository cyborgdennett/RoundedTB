using System;

namespace RoundedTB;

internal static class SettingsSanitizer
{
    internal static void Normalize(Types.Settings settings)
    {
        settings.AppOpenRevealSeconds = FiniteRange(settings.AppOpenRevealSeconds, 0.1, 60, 1);
        settings.AppAttentionRevealSeconds = FiniteRange(settings.AppAttentionRevealSeconds, 0.1, 60, 5);
        foreach (var layout in new[] { settings.SimpleTaskbarLayout, settings.DynamicAppListLayout, settings.DynamicTrayLayout, settings.DynamicWidgetsLayout })
        {
            if (layout == null) continue;
            layout.HoverHideDelaySeconds = FiniteRange(layout.HoverHideDelaySeconds, 0, 60, 0);
            layout.HoverPillWidth = Math.Clamp(layout.HoverPillWidth, 16, 600);
            layout.HoverPillThickness = Math.Clamp(layout.HoverPillThickness, 1, 12);
            layout.HoverOpenDurationMs = Math.Clamp(layout.HoverOpenDurationMs, 0, 5000);
            layout.HoverCloseDurationMs = Math.Clamp(layout.HoverCloseDurationMs, 0, 5000);
            layout.CornerRadius = Math.Max(0, layout.CornerRadius);
            if (!Enum.IsDefined(layout.HoverOpenEffect)) layout.HoverOpenEffect = Types.HoverAnimationEffect.None;
            if (!Enum.IsDefined(layout.HoverCloseEffect)) layout.HoverCloseEffect = Types.HoverAnimationEffect.None;
            if (layout.MaximizedVisibility.HasValue && !Enum.IsDefined(layout.MaximizedVisibility.Value)) layout.MaximizedVisibility = null;
        }
        foreach (var app in new[] { settings.SimpleTaskbarLayout, settings.DynamicAppListLayout })
            if (app?.MaximizedVisibility == Types.VisibilityMode.Hidden) app.MaximizedVisibility = Types.VisibilityMode.AutoHide;
    }

    private static double FiniteRange(double value, double minimum, double maximum, double fallback)
        => Math.Clamp(double.IsFinite(value) ? value : fallback, minimum, maximum);
}
