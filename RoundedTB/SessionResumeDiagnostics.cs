using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace RoundedTB;

// Records native geometry around session changes without changing window state.
internal static class SessionResumeDiagnostics
{
    private sealed record NativeWrite(long Timestamp, DateTime Utc, string Operation, long Window,
        LocalPInvoke.RECT Bounds, long Result);
    private static readonly ConcurrentQueue<NativeWrite> Writes = new();
    private static readonly object FileLock = new();
    private static int captureSequence;

    internal static void RecordWrite(string operation, IntPtr window, LocalPInvoke.RECT bounds, long result)
    {
        Writes.Enqueue(new NativeWrite(Environment.TickCount64, DateTime.UtcNow, operation, window.ToInt64(), bounds, result));
        while (Writes.Count > 128) Writes.TryDequeue(out _);
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out LocalPInvoke.RECT rect);

    internal static void Capture(MainWindow main, int reason)
    {
        _ = Task.Run(() => CaptureCore(main, reason));
    }

    private static void CaptureCore(MainWindow main, int reason)
    {
        if (!OperatingSystem.IsWindows() || main.taskbarDetails.Count == 0
            || !WorkAreaExpansion.Enabled(main.activeSettings) || main.activeSettings.IsWindowsDefault) return;
        try
        {
            var windows = new List<IntPtr>();
            LocalPInvoke.EnumWindows((window, _) =>
            {
                var placement = LocalPInvoke.WINDOWPLACEMENT.Default;
                var name = new StringBuilder(256);
                LocalPInvoke.GetClassName(window, name, name.Capacity);
                if (LocalPInvoke.IsWindowVisible(window) && LocalPInvoke.GetWindowPlacement(window, ref placement)
                    && (MaximizedWindowTaskbarAccess.Eligible(LocalPInvoke.GetWindowStyleBits(window, -16), placement.ShowCmd)
                        || name.ToString() == "Chrome_WidgetWin_1")) windows.Add(window);
                return true;
            }, IntPtr.Zero);
            var bars = main.taskbarDetails.Select(bar => bar.TaskbarHwnd).ToArray();
            long started = Environment.TickCount64;
            var watch = Stopwatch.StartNew();
            var samples = new List<object>();
            samples.Add(Snapshot(main, windows, bars, watch.ElapsedMilliseconds));
            _ = Task.Run(async () =>
            {
                try
                {
                    while (watch.ElapsedMilliseconds < 3000)
                    {
                        await Task.Delay(20);
                        samples.Add(Snapshot(main, windows, bars, watch.ElapsedMilliseconds));
                    }
                    var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoundedTB", "Diagnostics");
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, $"session-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{System.Threading.Interlocked.Increment(ref captureSequence)}-{reason}.json");
                    var json = JsonConvert.SerializeObject(new { Reason = reason, StartedTick = started, Writes = Writes.Where(write => write.Timestamp >= started - 500).ToArray(), Samples = samples }, Formatting.Indented);
                    lock (FileLock)
                    {
                        File.WriteAllText(path, json);
                        foreach (var old in new DirectoryInfo(directory).GetFiles("session-*.json").OrderByDescending(file => file.LastWriteTimeUtc).Skip(6))
                            try { old.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                    main.interaction?.AddLog("Session diagnostic saved: " + path);
                }
                catch (Exception error) { main.interaction?.AddLog("Session diagnostic failed: " + error.Message); }
            });
        }
        catch (Exception error) { main.interaction?.AddLog("Session diagnostic failed: " + error.Message); }
    }

    private static object Snapshot(MainWindow main, List<IntPtr> windows, IntPtr[] bars, long elapsed)
    {
        return new
        {
            Milliseconds = elapsed, Locked = main.SessionLocked,
            Foreground = LocalPInvoke.GetForegroundWindow().ToInt64(),
            Displays = MonitorStuff.GetDisplays().Select(display => new { display.MonitorArea, display.WorkArea }).ToArray(),
            Windows = windows.Select(window =>
            {
                var placement = LocalPInvoke.WINDOWPLACEMENT.Default;
                var name = new StringBuilder(256);
                LocalPInvoke.GetClassName(window, name, name.Capacity);
                LocalPInvoke.GetWindowPlacement(window, ref placement);
                LocalPInvoke.GetWindowRect(window, out var outer);
                GetClientRect(window, out var client);
                var frameResult = LocalPInvoke.DwmGetWindowFrameBounds(window, LocalPInvoke.DWMWINDOWATTRIBUTE.ExtendedFrameBounds, out var frame, 16);
                return new { Handle = window.ToInt64(), Class = name.ToString(), State = placement.ShowCmd, Outer = outer, Client = client, Frame = frame, FrameResult = frameResult };
            }).ToArray(),
            Taskbars = bars.Select(window =>
            {
                LocalPInvoke.GetWindowRect(window, out var outer);
                var appbar = new LocalPInvoke.APPBARDATA { cbSize = (uint)Marshal.SizeOf<LocalPInvoke.APPBARDATA>(), hWnd = window };
                long nativeState = LocalPInvoke.SHAppBarMessage(LocalPInvoke.ABM.GetState, ref appbar).ToInt64();
                bool layered = LocalPInvoke.GetLayeredWindowAttributes(window, out _, out var alpha, out _);
                var region = LocalPInvoke.CreateRectRgn(0, 0, 0, 0);
                try
                {
                    var kind = LocalPInvoke.GetWindowRgn(window, region);
                    LocalPInvoke.GetRgnBox(region, out var bounds);
                    return new { Handle = window.ToInt64(), Outer = outer, NativeState = nativeState, Layered = layered, Alpha = alpha, RegionType = kind, Region = bounds };
                }
                finally { LocalPInvoke.DeleteObject(region); }
            }).ToArray()
        };
    }
}
