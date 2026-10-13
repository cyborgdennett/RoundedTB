using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace RoundedTB;

// Chromium shortens a background fullscreen window by one pixel when another
// window takes focus. Restore its client size using normal window-position messages,
// which also reset Chromium's background_fullscreen_hack_ and compositor bounds.
internal sealed class FullscreenWindowBounds
{
    private readonly Dictionary<IntPtr, long> lastChecks = new();

    internal static bool HasBottomGap(string windowClass, uint style, LocalPInvoke.RECT window, LocalPInvoke.RECT monitor)
        => windowClass == "Chrome_WidgetWin_1" && (style & 0x00C00000) == 0
            && window.Left == monitor.Left && window.Top == monitor.Top && window.Right == monitor.Right
            && (long)monitor.Bottom - window.Bottom is >= 1 and <= 2;

    internal static bool IsFullscreen(string windowClass, uint style, LocalPInvoke.RECT window, LocalPInvoke.RECT monitor)
        => windowClass is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            && !Taskbar.IsShellOverlayClass(windowClass)
            && (style & 0x00C00000) == 0
            && window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right
            && window.Bottom >= monitor.Bottom - 2;

    internal static bool ForegroundIsFullscreen(IntPtr taskbar, LocalPInvoke.RECT monitor)
    {
        var foreground = LocalPInvoke.GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == taskbar || !LocalPInvoke.IsWindowVisible(foreground)
            || LocalPInvoke.IsIconic(foreground)
            || LocalPInvoke.MonitorFromWindow(foreground, 2) != LocalPInvoke.MonitorFromWindow(taskbar, 2)
            || !LocalPInvoke.GetWindowRect(foreground, out var bounds)) return false;
        var name = new StringBuilder(64);
        LocalPInvoke.GetClassName(foreground, name, name.Capacity);
        return IsFullscreen(name.ToString(), LocalPInvoke.GetWindowStyleBits(foreground, -16), bounds, monitor);
    }

    internal void Update(IntPtr taskbar, IEnumerable<IntPtr> windows)
    {
        long now = Environment.TickCount64;
        // Check at the worker's frame cadence so focus transitions do not leave
        // Chromium's one-pixel background gap visible for several frames.
        if (lastChecks.TryGetValue(taskbar, out long last) && now - last < 8) return;
        lastChecks[taskbar] = now;
        var monitor = new MonitorStuff.MONITORINFO { cbSize = (uint)Marshal.SizeOf<MonitorStuff.MONITORINFO>() };
        if (!MonitorStuff.GetMonitorInfo(LocalPInvoke.MonitorFromWindow(taskbar, 2), ref monitor)) return;
        foreach (var window in windows)
        {
            if (!LocalPInvoke.IsWindowVisible(window) || LocalPInvoke.IsIconic(window)
                || !LocalPInvoke.GetWindowRect(window, out var bounds)) continue;
            var name = new StringBuilder(64);
            LocalPInvoke.GetClassName(window, name, name.Capacity);
            if (!HasBottomGap(name.ToString(), LocalPInvoke.GetWindowStyleBits(window, -16), bounds, monitor.rcMonitor)) continue;
            if (LocalPInvoke.DwmGetWindowAttribute(window, LocalPInvoke.DWMWINDOWATTRIBUTE.Cloaked, out bool cloaked, 4) != 0 || cloaked) continue;
            LocalPInvoke.SetWindowPos(window, IntPtr.Zero, bounds.Left, bounds.Top,
                monitor.rcMonitor.Right - monitor.rcMonitor.Left, monitor.rcMonitor.Bottom - monitor.rcMonitor.Top,
                LocalPInvoke.SetWindowPosFlags.IgnoreZOrder | LocalPInvoke.SetWindowPosFlags.DoNotActivate);
        }
    }
}
