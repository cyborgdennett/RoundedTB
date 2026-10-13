using System;
using System.Collections.Generic;

namespace RoundedTB;

// The shell's fullscreen heuristic otherwise hides its activation edge when a
// maximized window occupies the whole monitor. NonRudeHWND is documented at:
// https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-itaskbarlist2-markfullscreenwindow
internal sealed class MaximizedWindowTaskbarAccess
{
    private const string Property = "NonRudeHWND";
    private const string OwnerProperty = "RoundedTB.NonRudeOwner";
    private readonly IntPtr owner = new(Environment.ProcessId);
    private readonly Dictionary<IntPtr, uint> marked = new();
    private long lastScan;

    internal static bool Eligible(uint style, LocalPInvoke.ShowWindowCommands state)
        => state == LocalPInvoke.ShowWindowCommands.Maximize && (style & 0x00C00000) == 0x00C00000;

    internal void Update(bool enabled)
    {
        if (!enabled) { Close(); return; }
        long now = Environment.TickCount64;
        if (now - lastScan < 50) return;
        lastScan = now;
        var eligible = new HashSet<IntPtr>();
        LocalPInvoke.EnumWindows((window, _) =>
        {
            if (!LocalPInvoke.IsWindowVisible(window)) return true;
            var placement = LocalPInvoke.WINDOWPLACEMENT.Default;
            if (!LocalPInvoke.GetWindowPlacement(window, ref placement)
                || !Eligible(LocalPInvoke.GetWindowStyleBits(window, -16), placement.ShowCmd)) return true;
            eligible.Add(window);
            if (marked.ContainsKey(window) || LocalPInvoke.GetProp(window, Property) != IntPtr.Zero
                || LocalPInvoke.GetProp(window, OwnerProperty) != IntPtr.Zero) return true;
            LocalPInvoke.GetWindowThreadProcessId(window, out uint processId);
            if (LocalPInvoke.SetProp(window, OwnerProperty, owner))
            {
                if (LocalPInvoke.SetProp(window, Property, new IntPtr(1))) marked.Add(window, processId);
                else LocalPInvoke.RemoveProp(window, OwnerProperty);
            }
            return true;
        }, IntPtr.Zero);
        foreach (var window in new List<IntPtr>(marked.Keys))
            if (!eligible.Contains(window)) Restore(window);
    }

    private void Restore(IntPtr window)
    {
        LocalPInvoke.GetWindowThreadProcessId(window, out uint processId);
        if (processId == marked[window] && LocalPInvoke.GetProp(window, OwnerProperty) == owner)
        {
            if (LocalPInvoke.GetProp(window, Property) == new IntPtr(1)) LocalPInvoke.RemoveProp(window, Property);
            LocalPInvoke.RemoveProp(window, OwnerProperty);
        }
        marked.Remove(window);
    }

    internal void Close()
    {
        foreach (var window in new List<IntPtr>(marked.Keys)) Restore(window);
        lastScan = 0;
    }
}
