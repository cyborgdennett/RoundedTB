using System;
using System.Collections.Generic;

namespace RoundedTB;

internal sealed class AppOpenReveal
{
    private HashSet<IntPtr> known;
    private long lastScan;

    internal static bool Active(Types.Taskbar taskbar, Types.Settings settings, long? timestamp = null)
        => SegmentHover.AppSettings(WindowModeVisibility.Resolve(settings, taskbar.HasMaximizedWindow))?.ShowOnHover == true && settings.RevealOnAppOpen
            && taskbar.AppOpenRevealStartedAt.HasValue
            && (timestamp ?? Environment.TickCount64) - taskbar.AppOpenRevealStartedAt.Value < DurationMilliseconds(settings);

    internal void Update(List<Types.Taskbar> taskbars, Types.Settings settings)
    {
        if (settings.IsWindowsDefault || !WindowModeVisibility.AnyAppAutoHide(settings) || !settings.RevealOnAppOpen)
        {
            Reset();
            foreach (var taskbar in taskbars) taskbar.AppOpenRevealStartedAt = null;
            return;
        }
        long now = Environment.TickCount64;
        Expire(taskbars, settings, now);
        if (known != null && now - lastScan < 100) return;
        lastScan = now;
        var windows = new HashSet<IntPtr>();
        LocalPInvoke.EnumWindows((window, _) =>
        {
            if (!LocalPInvoke.IsWindowVisible(window)) return true;
            uint style = LocalPInvoke.GetWindowStyleBits(window, -16);
            uint extended = LocalPInvoke.GetWindowStyleBits(window, LocalPInvoke.GWL_EXSTYLE);
            if ((style & 0x00C00000) != 0x00C00000 || (extended & 0x80) != 0
                || (LocalPInvoke.GetWindow(window, 4) != IntPtr.Zero && (extended & 0x40000) == 0)) return true;
            LocalPInvoke.GetWindowThreadProcessId(window, out uint process);
            if (process == Environment.ProcessId) return true;
            if (LocalPInvoke.DwmGetWindowAttribute(window, LocalPInvoke.DWMWINDOWATTRIBUTE.Cloaked,
                out bool cloaked, 4) == 0 && cloaked) return true;
            windows.Add(window);
            if (known != null && !known.Contains(window))
            {
                var monitor = LocalPInvoke.MonitorFromWindow(window, 2);
                foreach (var taskbar in taskbars)
                    if (LocalPInvoke.MonitorFromWindow(taskbar.TaskbarHwnd, 2) == monitor)
                        taskbar.AppOpenRevealStartedAt = now;
            }
            return true;
        }, IntPtr.Zero);
        // Seed existing windows without revealing on startup or when enabling the option.
        known = windows;
    }

    internal static long DurationMilliseconds(Types.Settings settings)
        => (long)(Math.Clamp(double.IsFinite(settings.AppOpenRevealSeconds) ? settings.AppOpenRevealSeconds : 1, 0.1, 60) * 1000);

    internal static void Expire(List<Types.Taskbar> taskbars, Types.Settings settings, long now)
    {
        foreach (var taskbar in taskbars)
            if (taskbar.AppOpenRevealStartedAt.HasValue
                && now - taskbar.AppOpenRevealStartedAt.Value >= DurationMilliseconds(settings))
                taskbar.AppOpenRevealStartedAt = null;
    }

    internal void Reset() { known = null; lastScan = 0; }
}
