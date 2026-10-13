using System;
using System.Collections.Generic;

namespace RoundedTB;

internal sealed class AppAttentionReveal
{
    internal const int Flash = 0x8006;
    private readonly object gate = new();
    private readonly Dictionary<IntPtr, long> firstFlashes = new();
    private bool keepOpen;
    private long duration = 5000;

    internal static bool Enabled(Types.Settings settings)
        => !settings.IsWindowsDefault && settings.RevealOnAppAttention
            && WindowModeVisibility.AnyAppAutoHide(settings);

    internal static long DurationMilliseconds(Types.Settings settings)
        => (long)(Math.Clamp(double.IsFinite(settings.AppAttentionRevealSeconds) ? settings.AppAttentionRevealSeconds : 5, 0.1, 60) * 1000);

    internal void Notify(int code, IntPtr window, Types.Settings settings, long? timestamp = null)
    {
        lock (gate)
        {
            if (!Enabled(settings)) { firstFlashes.Clear(); return; }
            keepOpen = settings.KeepAppAttentionOpenUntilAccepted;
            duration = DurationMilliseconds(settings);
            if (window == IntPtr.Zero) return;
            if (code == Flash) firstFlashes.TryAdd(window, timestamp ?? Environment.TickCount64);
            else if (code is 2 or 4 or 0x8004) firstFlashes.Remove(window); // Destroyed or activated.
        }
    }

    internal bool Active(IntPtr monitor, long now, Func<IntPtr, bool> exists, Func<IntPtr, IntPtr> monitorOf)
    {
        lock (gate)
        {
            bool active = false;
            foreach (var window in new List<IntPtr>(firstFlashes.Keys))
            {
                if (!exists(window)) firstFlashes.Remove(window);
                else if ((keepOpen || now - firstFlashes[window] < duration) && monitorOf(window) == monitor) active = true;
            }
            return active;
        }
    }

    internal void Update(List<Types.Taskbar> taskbars, Types.Settings settings)
    {
        bool enabled = Enabled(settings);
        if (!enabled) Clear();
        else Notify(4, LocalPInvoke.GetForegroundWindow(), settings);
        long now = Environment.TickCount64;
        foreach (var taskbar in taskbars)
        {
            bool active = enabled && SegmentHover.AppSettings(WindowModeVisibility.Resolve(settings, taskbar.HasMaximizedWindow))?.ShowOnHover == true && Active(LocalPInvoke.MonitorFromWindow(taskbar.TaskbarHwnd, 2), now,
                LocalPInvoke.IsWindow, window => LocalPInvoke.MonitorFromWindow(window, 2));
            if (active != taskbar.AppAttentionRevealActive) taskbar.Ignored = true;
            taskbar.AppAttentionRevealActive = active;
        }
    }

    internal void Clear() { lock (gate) firstFlashes.Clear(); }
}
