using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace RoundedTB;

internal readonly record struct ActivationStripSpec(IntPtr Taskbar, bool Tray, int Left, int Top, int Width, int Height, double Scale);

// The native taskbar region remains the input surface. These small, nonactivating
// windows only supply its appearance and never intercept native mouse input.
internal sealed class ActivationStripSurfaces
{
    private readonly Dictionary<(IntPtr, bool), ActivationStripSurface> windows = new();
    private readonly object gate = new();
    private ActivationStripSpec[] latest = Array.Empty<ActivationStripSpec>();
    private bool queued, closeRequested;

    internal static ActivationStripSpec[] Describe(IReadOnlyList<Types.Taskbar> taskbars, Types.Settings settings)
    {
        if (settings.IsWindowsDefault || !settings.IsDynamic) return Array.Empty<ActivationStripSpec>();
        var result = new List<ActivationStripSpec>();
        foreach (var taskbar in taskbars)
        {
            if (taskbar.AppearanceRestored || taskbar.TaskbarHidden) continue;
            var effective = WindowModeVisibility.Resolve(settings, taskbar.HasMaximizedWindow);
            Add(taskbar.AppListRect, effective.DynamicAppListLayout, taskbar.AppHoverTransition, false);
            if (taskbar.TrayHwnd != IntPtr.Zero)
                Add(taskbar.TrayRect, effective.DynamicTrayLayout, taskbar.TrayHoverTransition, true);
            void Add(LocalPInvoke.RECT segment, Types.SegmentSettings layout, HoverTransition transition, bool tray)
            {
                if (layout?.ShowOnHover != true || !layout.HoverActivationPill
                    || (transition.Initialized && transition.Progress >= 1)) return;
                var bounds = SegmentHover.ActivationPill(segment, taskbar, layout);
                if (bounds.Right <= bounds.Left) return;
                result.Add(new(taskbar.TaskbarHwnd, tray, bounds.Left, bounds.Top,
                    bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
                    taskbar.ScaleFactor > 0 ? taskbar.ScaleFactor : 1));
            }
        }
        return result.ToArray();
    }

    internal void Update(ActivationStripSpec[] desired)
    {
        lock (gate)
        {
            if (latest.AsSpan().SequenceEqual(desired)) return;
            latest = desired;
            Queue();
        }
    }

    internal void Close()
    {
        lock (gate)
        {
            latest = Array.Empty<ActivationStripSpec>();
            closeRequested = true;
            if (!Dispatcher.UIThread.CheckAccess()) Queue();
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply();
    }

    private void Queue()
    {
        if (queued) return;
        queued = true;
        Dispatcher.UIThread.Post(Apply, DispatcherPriority.Render);
    }

    private void Apply()
    {
        ActivationStripSpec[] desired;
        bool close;
        lock (gate)
        {
            desired = latest;
            close = closeRequested;
            closeRequested = queued = false;
        }
        if (close)
        {
            foreach (var window in windows.Values) window.Close();
            windows.Clear();
        }
        foreach (var entry in windows)
        {
            if (!Array.Exists(desired, spec => (spec.Taskbar, spec.Tray) == entry.Key)) entry.Value.Hide();
        }
        foreach (var spec in desired)
        {
            var key = (spec.Taskbar, spec.Tray);
            if (!windows.TryGetValue(key, out var surface))
                windows.Add(key, surface = new ActivationStripSurface());
            surface.Place(spec);
        }
    }
}

internal sealed class ActivationStripSurface : Window
{
    private readonly Border surface;

    internal ActivationStripSurface()
    {
        Title = "RoundedTB activation strip";
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        IsHitTestVisible = false;
        surface = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.Parse("#202020")),
            Child = new Border
            {
                Background = Brushes.White,
                Height = 1,
                Margin = new Thickness(6, 0),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Content = surface;
        Opened += (_, _) =>
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero) return;
            const int noActivate = 0x08000000, toolWindow = 0x00000080;
            var style = LocalPInvoke.GetWindowLong(handle, LocalPInvoke.GWL_EXSTYLE).ToInt32();
            LocalPInvoke.SetWindowLong(handle, LocalPInvoke.GWL_EXSTYLE,
                style | noActivate | toolWindow | LocalPInvoke.WS_EX_TRANSPARENT);
        };
    }

    internal void Place(ActivationStripSpec spec)
    {
        Position = new PixelPoint(spec.Left, spec.Top);
        Width = spec.Width / spec.Scale;
        Height = spec.Height / spec.Scale;
        // Windows can impose a larger minimum layout size on a top-level
        // window. Keep the drawing sized to the strip even in that case.
        surface.Width = Width;
        surface.Height = Height;
        var radius = Height / 2;
        surface.CornerRadius = new CornerRadius(radius, radius, 0, 0);
        if (!IsVisible) Show();
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
            LocalPInvoke.SetWindowPos(handle, new IntPtr(-1), spec.Left, spec.Top, spec.Width, spec.Height,
                LocalPInvoke.SetWindowPosFlags.DoNotActivate);
    }
}
