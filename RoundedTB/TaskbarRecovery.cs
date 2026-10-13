using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Newtonsoft.Json;

namespace RoundedTB;

// A separate process is needed because TerminateProcess cannot run exit handlers.
internal static class TaskbarRecovery
{
    private const string CompletionEvent = @"Local\RoundedTB.Recovery.Complete";
    private sealed class Snapshot
    {
        public int ProcessId { get; set; }
        public long StartTime { get; set; }
        public int AppBarState { get; set; }
        public Area[] Areas { get; set; }
    }
    private sealed class Area
    {
        public LocalPInvoke.RECT Monitor { get; set; }
        public LocalPInvoke.RECT Work { get; set; }
    }

    internal static void Start()
    {
        using var completed = new EventWaitHandle(true, EventResetMode.ManualReset, CompletionEvent);
        if (!completed.WaitOne(TimeSpan.FromSeconds(10)))
            throw new InvalidOperationException("The previous taskbar recovery has not completed.");
        using var parent = Process.GetCurrentProcess();
        var bar = LocalPInvoke.FindWindowExA(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
        var data = new LocalPInvoke.APPBARDATA { cbSize = (uint)Marshal.SizeOf<LocalPInvoke.APPBARDATA>(), hWnd = bar };
        var snapshot = new Snapshot
        {
            ProcessId = parent.Id, StartTime = parent.StartTime.ToUniversalTime().Ticks,
            AppBarState = (int)LocalPInvoke.SHAppBarMessage(LocalPInvoke.ABM.GetState, ref data),
            Areas = MonitorStuff.GetDisplays().Select(display => new Area { Monitor = display.MonitorArea, Work = display.WorkArea }).ToArray()
        };
        // Recover the reservation if an older force-killed build left an
        // expanded work area behind before this helper was available.
        if ((snapshot.AppBarState & (int)LocalPInvoke.AppBarStates.AutoHide) == 0)
            foreach (var taskbar in Taskbar.GenerateTaskbarInfo(enableEffects: false))
                foreach (var area in snapshot.Areas)
                {
                    var rect = taskbar.TaskbarRect;
                    if (!area.Work.Equals(area.Monitor) || rect.Left != area.Monitor.Left
                        || rect.Right != area.Monitor.Right || rect.Bottom != area.Monitor.Bottom
                        || rect.Bottom - rect.Top <= 2 || rect.Bottom - rect.Top >= (area.Monitor.Bottom - area.Monitor.Top) / 2) continue;
                    var work = area.Work;
                    work.Bottom = rect.Top;
                    area.Work = work;
                }
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(Path.GetTempPath(), "RoundedTB-recovery-" + id + ".json");
        var readyName = @"Local\RoundedTB.Recovery.Ready." + id;
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, readyName);
        File.WriteAllText(path, JsonConvert.SerializeObject(snapshot));
        completed.Reset();
        try
        {
            var helperPath = Path.Combine(AppContext.BaseDirectory, "RoundedTB.Recovery.exe");
            var start = new ProcessStartInfo(helperPath) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--recover-taskbar");
            start.ArgumentList.Add(path);
            start.ArgumentList.Add(readyName);
            using var helper = Process.Start(start);
            if (!ready.WaitOne(TimeSpan.FromSeconds(10)))
                throw new InvalidOperationException("The taskbar recovery helper could not start.");
        }
        catch
        {
            completed.Set();
            throw;
        }
    }

    internal static int Run(string path, string readyName)
    {
        using var completed = EventWaitHandle.OpenExisting(CompletionEvent);
        try
        {
            var snapshot = JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(path));
            using var ready = EventWaitHandle.OpenExisting(readyName);
            Process parent = null;
            try
            {
                parent = Process.GetProcessById(snapshot.ProcessId);
                // Open the actual process handle before signalling readiness, and
                // verify identity so a reused PID cannot delay or misdirect recovery.
                _ = parent.Handle;
                if (parent.StartTime.ToUniversalTime().Ticks != snapshot.StartTime) { parent.Dispose(); parent = null; }
            }
            catch (ArgumentException) { parent?.Dispose(); parent = null; }
            ready.Set();
            using (parent) parent?.WaitForExit();
            Restore(snapshot);
            return 0;
        }
        catch (Exception error)
        {
            try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rtb.log"),
                $"[{DateTime.Now:O}] Taskbar recovery failed: {error}{Environment.NewLine}"); } catch { }
            return 1;
        }
        finally
        {
            completed.Set();
            try { File.Delete(path); } catch { }
        }
    }

    private static void Restore(Snapshot snapshot)
    {
        foreach (var taskbar in Taskbar.GenerateTaskbarInfo(enableEffects: false))
        {
            Taskbar.ResetTaskbar(taskbar, new Types.Settings());
            Taskbar.SetTaskbarState((LocalPInvoke.AppBarStates)snapshot.AppBarState, taskbar.TaskbarHwnd);
        }
        foreach (var display in MonitorStuff.GetDisplays())
        {
            var saved = snapshot.Areas.FirstOrDefault(area => area.Monitor.Equals(display.MonitorArea));
            if (saved == null || !display.WorkArea.Equals(display.MonitorArea)) continue;
            var work = saved.Work;
            LocalPInvoke.SystemParametersInfo(LocalPInvoke.SPI_SETWORKAREA, 0, ref work, 2);
        }
        var owner = new IntPtr(snapshot.ProcessId);
        LocalPInvoke.EnumWindows((window, _) =>
        {
            if (LocalPInvoke.GetProp(window, "RoundedTB.NonRudeOwner") != owner) return true;
            if (LocalPInvoke.GetProp(window, "NonRudeHWND") == new IntPtr(1)) LocalPInvoke.RemoveProp(window, "NonRudeHWND");
            LocalPInvoke.RemoveProp(window, "RoundedTB.NonRudeOwner");
            return true;
        }, IntPtr.Zero);
    }
}
