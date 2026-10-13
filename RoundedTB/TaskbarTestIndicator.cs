using System;
using System.Runtime.InteropServices;

namespace RoundedTB;

// This test indicator belongs only to RoundedTB's settings-window taskbar button.
internal sealed class TaskbarTestIndicator : IDisposable
{
    [ComImport, Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr window);
        void DeleteTab(IntPtr window);
        void ActivateTab(IntPtr window);
        void SetActiveAlt(IntPtr window);
        void MarkFullscreenWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr window, ulong completed, ulong total);
        void SetProgressState(IntPtr window, uint state);
    }

    private ITaskbarList3 taskbar;
    private IntPtr window;
    internal void Show(IntPtr handle, int style)
    {
        Clear();
        if (style == 0) return;
        taskbar = (ITaskbarList3)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11D0-958A-006097C9A090")));
        window = handle;
        taskbar.HrInit();
        taskbar.SetProgressValue(window, 1, 1);
        taskbar.SetProgressState(window, style == 1 ? 8u : 4u); // Yellow paused / red error.
    }

    internal void Clear()
    {
        if (taskbar == null) return;
        try { taskbar.SetProgressState(window, 0); }
        finally { Marshal.ReleaseComObject(taskbar); taskbar = null; window = IntPtr.Zero; }
    }

    public void Dispose() => Clear();
}
