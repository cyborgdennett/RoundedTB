using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Interop.UIAutomationClient;

namespace RoundedTB;

internal sealed class WindowsApiDiagnostics
{
    internal record Result(string Name, string Status, string Detail);
    private readonly List<Result> results = new();

    internal static Result Check(string name, Action action)
    {
        try { action(); return new(name, "PASS", "Call accepted and checked."); }
        catch (Win32Exception error) { return new(name, error.NativeErrorCode == 5 ? "ACCESS DENIED" : "FAIL", $"Windows error {error.NativeErrorCode}: {error.Message}"); }
        catch (UnauthorizedAccessException error) { return new(name, "ACCESS DENIED", error.Message); }
        catch (COMException error) { return new(name, error.HResult == unchecked((int)0x80070005) ? "ACCESS DENIED" : "FAIL", $"HRESULT 0x{error.HResult:X8}: {error.Message}"); }
        catch (Exception error) { return new(name, "FAIL", error.Message); }
    }

    private void Probe(string name, Action action) => results.Add(Check(name, action));
    private void Skip(string name, string reason) => results.Add(new(name, "NOT VERIFIED", reason));
    private static void Require(bool success)
    {
        if (!success)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error == 0) throw new InvalidOperationException("Windows returned failure without an extended error code.");
            throw new Win32Exception(error);
        }
    }

    internal string Run()
    {
        if (!OperatingSystem.IsWindows()) return "Windows API diagnostics are available only on Windows.";
        Probe("Cursor access / GetCursorPos", () => Require(LocalPInvoke.GetCursorPos(out _)));
        Probe("GDI clipping / CreateRoundRectRgn, CombineRgn, GetRgnBox", () =>
        {
            var rounded = LocalPInvoke.CreateRoundRectRgn(0, 0, 100, 40, 8, 8);
            var copy = LocalPInvoke.CreateRectRgn(0, 0, 0, 0);
            try
            {
                if (rounded == IntPtr.Zero || copy == IntPtr.Zero) throw new InvalidOperationException("Region allocation failed.");
                if (LocalPInvoke.CombineRgn(copy, rounded, IntPtr.Zero, 5) == 0
                    || LocalPInvoke.GetRgnBox(copy, out _) == 0) throw new InvalidOperationException("Region copy/read failed.");
            }
            finally
            {
                if (rounded != IntPtr.Zero) LocalPInvoke.DeleteObject(rounded);
                if (copy != IntPtr.Zero) LocalPInvoke.DeleteObject(copy);
            }
        });
        var displays = MonitorStuff.GetDisplays(); // Snapshot before any shell writes.
        var bars = new List<IntPtr>();
        var apps = new List<IntPtr>();
        Probe("Window discovery / EnumWindows", () => Require(LocalPInvoke.EnumWindows((window, _) =>
        {
            var name = new StringBuilder(256);
            LocalPInvoke.GetClassName(window, name, name.Capacity);
            if (name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") bars.Add(window);
            else if (LocalPInvoke.IsWindowVisible(window)
                && (LocalPInvoke.GetWindowStyleBits(window, -16) & 0x00C00000) == 0x00C00000)
            {
                LocalPInvoke.GetWindowThreadProcessId(window, out uint pid);
                if (pid != Environment.ProcessId) apps.Add(window);
            }
            return true;
        }, IntPtr.Zero)));
        if (bars.Count == 0) results.Add(new("Taskbar discovery", "FAIL", "Explorer taskbar was not found."));
        foreach (var window in bars)
        {
            string label = $"Taskbar 0x{window.ToInt64():X}";
            Probe(label + " / geometry and DPI", () =>
            {
                Require(LocalPInvoke.GetWindowRect(window, out _));
                if (LocalPInvoke.GetDpiForWindow(window) <= 0) throw new InvalidOperationException("No taskbar DPI returned.");
            });
            Probe(label + " / extended style write", () =>
            {
                uint style = LocalPInvoke.GetWindowStyleBits(window, LocalPInvoke.GWL_EXSTYLE);
                SetStyle(window, style); // Existing value: verifies cross-process write access without changing behavior.
                if (LocalPInvoke.GetWindowStyleBits(window, LocalPInvoke.GWL_EXSTYLE) != style)
                    throw new InvalidOperationException("Style readback did not match.");
            });
            Probe(label + " / opacity change and restore", () => TestOpacity(window));
            TestRegion(window, label);
            Probe(label + " / SetWindowPos", () => Require(LocalPInvoke.SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0,
                LocalPInvoke.SetWindowPosFlags.IgnoreMove | LocalPInvoke.SetWindowPosFlags.IgnoreResize
                | LocalPInvoke.SetWindowPosFlags.IgnoreZOrder | LocalPInvoke.SetWindowPosFlags.DoNotActivate
                | LocalPInvoke.SetWindowPosFlags.AsynchronousWindowPosition)));
            Probe(label + " / PostMessage", () => Require(LocalPInvoke.PostMessage(window, 0, IntPtr.Zero, IntPtr.Zero)));
            Probe(label + " / UI Automation", () =>
            {
                var ui = new CUIAutomation();
                IUIAutomationElement root = null;
                try { root = ui.ElementFromHandle(window); if (root == null) throw new InvalidOperationException("No automation element returned."); _ = root.CurrentProcessId; }
                finally { if (root != null) Marshal.ReleaseComObject(root); Marshal.ReleaseComObject(ui); }
            });
            TestProperty(window, label);
            // ABM_SETSTATE always returns TRUE. A no-op readback cannot prove that a different state would be accepted.
            var state = AppBars.GetAppbarState(window);
            AppBars.SetAppbarState(window, state);
            Skip(label + " / SHAppBarMessage", "Existing state reapplied; API has no failure result. Auto-hide was not toggled.");
        }
        if (displays.Count == 0) results.Add(new("Monitor discovery", "FAIL", "No monitors returned."));
        foreach (var display in displays)
        {
            var original = display.WorkArea;
            Probe($"Monitor 0x{display.Handle.ToInt64():X} / SPI_SETWORKAREA", () =>
            {
                var area = original;
                Require(LocalPInvoke.SystemParametersInfo(LocalPInvoke.SPI_SETWORKAREA, 0, ref area, 0));
                var current = MonitorStuff.GetDisplays().FirstOrDefault(d => d.Handle == display.Handle);
                if (current == null || !Same(current.WorkArea, original))
                {
                    var restore = original;
                    Require(LocalPInvoke.SystemParametersInfo(LocalPInvoke.SPI_SETWORKAREA, 0, ref restore, 0));
                    throw new InvalidOperationException("Work-area readback differed; original rectangle reapplied.");
                }
            });
        }
        foreach (var window in apps)
        {
            string label = $"App window 0x{window.ToInt64():X}";
            if (!LocalPInvoke.IsWindow(window)) { Skip(label, "Window closed during the test."); continue; }
            TestProperty(window, label);
            Probe(label + " / maximized-window inspection", () =>
            {
                var placement = LocalPInvoke.WINDOWPLACEMENT.Default;
                Require(LocalPInvoke.GetWindowPlacement(window, ref placement));
                Marshal.ThrowExceptionForHR(LocalPInvoke.DwmGetWindowAttribute(window,
                    LocalPInvoke.DWMWINDOWATTRIBUTE.Cloaked, out bool _, 4));
                int hr = LocalPInvoke.DwmGetWindowFrameBounds(window, LocalPInvoke.DWMWINDOWATTRIBUTE.ExtendedFrameBounds, out _, 16);
                Marshal.ThrowExceptionForHR(hr);
            });
        }
        Probe("RegisterHotKey / UnregisterHotKey", () =>
        {
            const int id = 9001;
            Require(LocalPInvoke.RegisterHotKey(IntPtr.Zero, id, 1 | 2 | 4, 0x87)); // Ctrl+Alt+Shift+F24, not the user's Win+F2 shortcut.
            Require(LocalPInvoke.UnregisterHotKey(IntPtr.Zero, id));
        });
        Skip("ShowWindow / foreground activation / synthetic hover", "Not exercised: changing focus or window state is not required for this permission test.");
        Skip("Third-party taskbar refresh / SendMessage", "Not sent: may block or change another app's appearance. This does not establish a permission failure.");
        return Format(results);
    }

    private static bool Same(LocalPInvoke.RECT a, LocalPInvoke.RECT b)
        => a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

    private static void SetStyle(IntPtr window, uint style)
    {
        Marshal.SetLastSystemError(0);
        int previous = LocalPInvoke.SetWindowLong(window, LocalPInvoke.GWL_EXSTYLE, unchecked((int)style));
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    private static void TestOpacity(IntPtr window)
    {
        uint style = LocalPInvoke.GetWindowStyleBits(window, LocalPInvoke.GWL_EXSTYLE);
        bool layered = (style & LocalPInvoke.WS_EX_LAYERED) != 0;
        uint key = 0, flags = LocalPInvoke.LWA_ALPHA;
        byte alpha = 255;
        if (layered) Require(LocalPInvoke.GetLayeredWindowAttributes(window, out key, out alpha, out flags));
        try
        {
            if (!layered) SetStyle(window, style | LocalPInvoke.WS_EX_LAYERED);
            byte testAlpha = alpha == 0 ? (byte)0 : (byte)(alpha - 1);
            Require(LocalPInvoke.SetLayeredWindowAttributes(window, key, testAlpha, flags | LocalPInvoke.LWA_ALPHA));
            Require(LocalPInvoke.GetLayeredWindowAttributes(window, out _, out byte actual, out _));
            if (actual != testAlpha) throw new InvalidOperationException("Opacity readback did not match.");
        }
        finally
        {
            // Restore even if the test fails. Failures here are included in the report.
            if (layered) Require(LocalPInvoke.SetLayeredWindowAttributes(window, key, alpha, flags));
            else SetStyle(window, style);
        }
    }

    private void TestRegion(IntPtr window, string label)
    {
        IntPtr region = LocalPInvoke.CreateRectRgn(0, 0, 0, 0);
        if (region == IntPtr.Zero) { results.Add(new(label + " / GDI region", "FAIL", "Could not allocate region.")); return; }
        try
        {
            if (LocalPInvoke.GetWindowRgn(window, region) == 0)
            {
                Skip(label + " / SetWindowRgn", "No readable clipping region; skipped to preserve the current appearance.");
                return;
            }
            Probe(label + " / SetWindowRgn", () =>
            {
                Require(LocalPInvoke.SetWindowRgn(window, region, false) != 0);
                region = IntPtr.Zero; // Windows owns a region after a successful SetWindowRgn.
            });
        }
        finally { if (region != IntPtr.Zero) LocalPInvoke.DeleteObject(region); }
    }

    private void TestProperty(IntPtr window, string label)
    {
        string property = "RoundedTB.Diagnostics." + Guid.NewGuid().ToString("N");
        Probe(label + " / SetProp, GetProp, RemoveProp", () =>
        {
            try
            {
                Require(LocalPInvoke.SetProp(window, property, new IntPtr(1)));
                if (LocalPInvoke.GetProp(window, property) != new IntPtr(1)) throw new InvalidOperationException("Property readback did not match.");
            }
            finally
            {
                if (LocalPInvoke.GetProp(window, property) != IntPtr.Zero)
                {
                    Require(LocalPInvoke.RemoveProp(window, property) != IntPtr.Zero);
                    if (LocalPInvoke.GetProp(window, property) != IntPtr.Zero) throw new InvalidOperationException("Test property could not be removed.");
                }
            }
        });
    }

    internal static string Format(IEnumerable<Result> results)
    {
        var entries = results.ToList();
        int denied = entries.Count(r => r.Status == "ACCESS DENIED");
        var text = new StringBuilder();
        text.AppendLine($"Windows API test — {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        text.AppendLine($"Passed: {entries.Count(r => r.Status == "PASS")}; access denied: {denied}; other failures: {entries.Count(r => r.Status == "FAIL")}; not verified: {entries.Count(r => r.Status == "NOT VERIFIED")}");
        text.AppendLine(denied > 0 ? "Windows denied some operations. Check the affected target; an elevated app can block access without preventing RoundedTB from controlling Explorer." : "No access-denied result was observed in the tested operations.");
        text.AppendLine("Tests use current values where possible and restore temporary opacity/properties. Passing does not guarantee every Windows feature or future window is accessible. No elevation or saved-setting changes are performed.");
        text.AppendLine();
        foreach (var entry in entries) text.AppendLine($"[{entry.Status}] {entry.Name}: {entry.Detail}");
        return text.ToString();
    }
}
