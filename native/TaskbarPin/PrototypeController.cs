using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

// Developer-only controller. The native watchdog restores the captured state
// if this process terminates, including termination without Dispose.
public sealed class TaskbarPinController : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8)]
    public struct Configuration
    {
        public uint Magic, Version, Size, Parent;
        public uint TaskbarTimestamp, TaskbarImageSize, ViewTimestamp, ViewImageSize;
        public ulong Hide, SecondaryHide, ShouldExpand, UpdateExpanded;
        public uint OriginalState;
        public int Enabled, Status;
        public uint Error;
        public long HideBlocked, ExpandForced;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string TaskbarModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ViewModule;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo { public uint Size; public Rect Bounds, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Appbar { public uint Size; public IntPtr Window; public uint Callback, Edge; public Rect Bounds; public IntPtr Param; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Placement { public uint Size, Flags, Show; public Point Min, Max; public Rect Normal; }
    public sealed class WindowGeometry { public long Handle; public string Class; public uint State; public Rect Outer, Client, Frame; public int FrameResult; }
    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibrary(string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string name, string title);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int kind, IntPtr callback, IntPtr module, uint thread);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr w, IntPtr l, uint flags, uint timeout, out UIntPtr result);
    [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message, ref Appbar data);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr window, ref Placement placement);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int size);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect rect, int size);

    public static long[] CaptureWindowHandles()
    {
        var result = new List<long>();
        EnumWindows(delegate(IntPtr window, IntPtr parameter) {
            var placement = new Placement { Size = (uint)Marshal.SizeOf(typeof(Placement)) };
            var name = new System.Text.StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            if (IsWindowVisible(window) && GetWindowPlacement(window, ref placement)
                && (placement.Show == 3 || name.ToString() == "Chrome_WidgetWin_1")) result.Add(window.ToInt64());
            return true;
        }, IntPtr.Zero);
        return result.ToArray();
    }

    public static WindowGeometry[] MeasureWindows(long[] handles)
    {
        var result = new List<WindowGeometry>();
        foreach (long handle in handles)
        {
            var window = new IntPtr(handle);
            var placement = new Placement { Size = (uint)Marshal.SizeOf(typeof(Placement)) };
            if (!GetWindowPlacement(window, ref placement)) continue;
            var name = new System.Text.StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            Rect outer, client, frame;
            GetWindowRect(window, out outer); GetClientRect(window, out client);
            int status = DwmGetWindowAttribute(window, 9, out frame, 16);
            result.Add(new WindowGeometry { Handle = handle, Class = name.ToString(), State = placement.Show, Outer = outer, Client = client, Frame = frame, FrameResult = status });
        }
        return result.ToArray();
    }

    private Mutex mutex;
    private MemoryMappedFile mapping;
    private MemoryMappedViewAccessor view;
    private IntPtr library, hook;
    private bool ownsMutex, requested, disposed;
#if !NET10_0_OR_GREATER
    private bool sessionEventsSubscribed;
#endif
    private int lockEvents, unlockEvents;
    public readonly IntPtr Taskbar;
    public readonly uint OriginalState;

    public static uint NativeState(IntPtr bar)
    {
        var data = new Appbar { Size = (uint)Marshal.SizeOf(typeof(Appbar)), Window = bar };
        return (uint)SHAppBarMessage(4, ref data).ToUInt64();
    }
    public static IntPtr FindTaskbar() { return FindWindow("Shell_TrayWnd", null); }
    private static void SetNativeState(IntPtr bar, uint state)
    {
        var data = new Appbar { Size = (uint)Marshal.SizeOf(typeof(Appbar)), Window = bar, Param = new IntPtr((long)state) };
        SHAppBarMessage(10, ref data);
    }

    public TaskbarPinController(string dll, Configuration configuration)
    {
        if (IntPtr.Size != 8 || Marshal.SizeOf(typeof(Configuration)) != 352) throw new InvalidOperationException("Requires the x64 352-byte native protocol.");
        Taskbar = FindWindow("Shell_TrayWnd", null);
        if (Taskbar == IntPtr.Zero) throw new InvalidOperationException("Taskbar unavailable.");
        OriginalState = NativeState(Taskbar);
        if ((OriginalState & 1) != 0) throw new InvalidOperationException("Start with native auto-hide off; another test might still be active.");
        try
        {
            mutex = new Mutex(false, @"Local\RoundedTB.TaskbarPin.Controller");
            try { ownsMutex = mutex.WaitOne(0); } catch (AbandonedMutexException) { ownsMutex = true; }
            if (!ownsMutex) throw new InvalidOperationException("A native pin controller is already running.");
            configuration.Magic = 0x50544252; configuration.Version = 1; configuration.Size = 352;
            configuration.Parent = (uint)Process.GetCurrentProcess().Id;
            configuration.OriginalState = OriginalState; configuration.Enabled = 1;
            configuration.Status = 0; configuration.Error = 0; configuration.HideBlocked = 0; configuration.ExpandForced = 0;
            mapping = MemoryMappedFile.CreateOrOpen(@"Local\RoundedTB.TaskbarPin." + configuration.Parent, 352, MemoryMappedFileAccess.ReadWrite);
            view = mapping.CreateViewAccessor(0, 352, MemoryMappedFileAccess.ReadWrite);
            var buffer = new byte[352];
            IntPtr memory = Marshal.AllocHGlobal(352);
            try { Marshal.StructureToPtr(configuration, memory, false); Marshal.Copy(memory, buffer, 0, buffer.Length); }
            finally { Marshal.FreeHGlobal(memory); }
            view.WriteArray(0, buffer, 0, buffer.Length);
            library = LoadLibrary(dll);
            if (library == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Load prototype DLL");
            IntPtr callback = GetProcAddress(library, "HookProc");
            if (callback == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Find HookProc");
            uint process;
            uint thread = GetWindowThreadProcessId(Taskbar, out process);
            using (var owner = Process.GetProcessById((int)process))
                if (!owner.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Taskbar owner is not Explorer.");
            hook = SetWindowsHookEx(4, callback, library, thread);
            if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Install taskbar-thread hook");
            UIntPtr result;
            requested = true;
            if (SendMessageTimeout(Taskbar, RegisterWindowMessage("RoundedTB.TaskbarPin.Start.1"), new UIntPtr(configuration.Parent), IntPtr.Zero, 2, 3000, out result) == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Start native pin prototype");
            var watch = Stopwatch.StartNew();
            while (Status == 0 && watch.ElapsedMilliseconds < 10000) Thread.Sleep(20);
            if (Status != 1) throw new Win32Exception(unchecked((int)Error), "Native initialization status=" + Status + ", error=" + Error);
            if (!UnhookWindowsHookEx(hook)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Remove temporary message hook");
            hook = IntPtr.Zero;
#if !NET10_0_OR_GREATER
            Microsoft.Win32.SystemEvents.SessionSwitch += SessionChanged;
            sessionEventsSubscribed = true;
#endif
            SetNativeState(Taskbar, OriginalState | 1);
            if ((NativeState(Taskbar) & 1) == 0) throw new InvalidOperationException("Native auto-hide was not enabled.");
        }
        catch { Dispose(); throw; }
    }

    public int Status { get { return view == null ? -1 : view.ReadInt32(72); } }
    public uint Error { get { return view == null ? 0 : view.ReadUInt32(76); } }
    public long HideBlocked { get { return view == null ? 0 : view.ReadInt64(80); } }
    public long ExpandForced { get { return view == null ? 0 : view.ReadInt64(88); } }
    public int LockEvents { get { return Interlocked.CompareExchange(ref lockEvents, 0, 0); } }
    public int UnlockEvents { get { return Interlocked.CompareExchange(ref unlockEvents, 0, 0); } }

#if !NET10_0_OR_GREATER
    private void SessionChanged(object sender, Microsoft.Win32.SessionSwitchEventArgs args)
    {
        if (args.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock) Interlocked.Increment(ref lockEvents);
        if (args.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) Interlocked.Increment(ref unlockEvents);
    }

#endif

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (view != null)
            {
                view.Write(68, 0);
                if (requested && FindTaskbar() == Taskbar)
                {
                    var watch = Stopwatch.StartNew();
                    while ((Status == 0 || Status == 1) && watch.ElapsedMilliseconds < 10000) Thread.Sleep(20);
                    if (Status != 2) Console.Error.WriteLine("Native cleanup status=" + Status + ", error=" + Error);
                }
            }
            var current = FindTaskbar();
            if (ownsMutex && current != IntPtr.Zero && NativeState(current) != OriginalState) SetNativeState(current, OriginalState);
        }
        finally
        {
#if !NET10_0_OR_GREATER
            if (sessionEventsSubscribed) Microsoft.Win32.SystemEvents.SessionSwitch -= SessionChanged;
#endif
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
            if (library != IntPtr.Zero) FreeLibrary(library);
            if (view != null) view.Dispose();
            if (mapping != null) mapping.Dispose();
            if (ownsMutex) mutex.ReleaseMutex();
            if (mutex != null) mutex.Dispose();
        }
    }
}
