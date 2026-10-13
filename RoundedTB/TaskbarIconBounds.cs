using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Interop.UIAutomationClient;

namespace RoundedTB;

internal static class TaskbarIconBounds
{
    private const int ControlTypeProperty = 30003;
    private const int ButtonControlType = 50000;
    [ThreadStatic] private static IUIAutomation automation;
    [ThreadStatic] private static Dictionary<IntPtr, ButtonTracker> trackers;

    public static bool TryGet(IntPtr handle, LocalPInvoke.RECT taskbar, out LocalPInvoke.RECT bounds)
        => TryGet(handle, taskbar, out bounds, out _);

    public static bool TryGet(IntPtr handle, LocalPInvoke.RECT taskbar, out LocalPInvoke.RECT bounds,
        out LocalPInvoke.RECT widgets)
    {
        bounds = default;
        widgets = default;
        try
        {
            automation ??= new CUIAutomation();
            trackers ??= new();
            if (!trackers.TryGetValue(handle, out var tracker))
            {
                tracker = new ButtonTracker(automation, handle);
                trackers.Add(handle, tracker);
            }
            return tracker.TryGet(taskbar, out bounds, out widgets);
        }
        catch (COMException)
        {
            if (trackers != null && trackers.Remove(handle, out var tracker)) tracker.Dispose();
            return false;
        }
    }

    internal static void Clear()
    {
        if (trackers != null)
        {
            foreach (var tracker in trackers.Values) tracker.Dispose();
            trackers.Clear();
        }
        Release(automation);
        automation = null;
    }

    // Cache element identities, never their positions. A full tree scan is too slow
    // for animation; two live edge reads take only a small part of a display frame.
    [ComVisible(true)]
    private sealed class ButtonTracker : IUIAutomationStructureChangedEventHandler, IDisposable
    {
        private readonly IUIAutomation ui;
        private readonly IUIAutomationElement root;
        private readonly IntPtr handle;
        private readonly List<IUIAutomationElement> retained = new();
        private IUIAutomationElement first, last, widget;
        private int revision, measuredRevision = -1, removalRevision, measuredRemovalRevision;
        private long lastDiscovery;
        private readonly CenteredTaskbarBounds centeredBounds = new();
        private bool firstIsStart, constrainedLayout;
        private int membershipDirection, notifiedRevision, notifiedRemovalRevision;
        private LocalPInvoke.RECT previousTaskbar;

        public ButtonTracker(IUIAutomation ui, IntPtr handle)
        {
            this.ui = ui;
            this.handle = handle;
            root = ui.ElementFromHandle(handle);
            try { ui.AddStructureChangedEventHandler(root, TreeScope.TreeScope_Subtree, null, this); }
            catch { Release(root); throw; }
        }

        public void HandleStructureChangedEvent(IUIAutomationElement sender, StructureChangeType changeType, int[] runtimeId)
        {
            int nextDirection = 0;
            try
            {
                if (sender?.CurrentClassName?.StartsWith("Taskbar.TaskList", StringComparison.Ordinal) == true)
                    nextDirection = changeType == StructureChangeType.StructureChangeType_ChildRemoved ? 1
                        : changeType == StructureChangeType.StructureChangeType_ChildAdded ? -1 : 0;
            }
            catch (COMException) { }
            // Tray and widgets also send structure events; they cannot reverse an
            // in-progress app-list animation.
            Volatile.Write(ref membershipDirection, nextDirection);
            if (changeType == StructureChangeType.StructureChangeType_ChildRemoved)
            {
                Interlocked.Increment(ref removalRevision);
            }
            else
            {
                Interlocked.Increment(ref revision);
            }
        }

        public bool TryGet(LocalPInvoke.RECT taskbar, out LocalPInvoke.RECT bounds, out LocalPInvoke.RECT widgets)
        {
            if (first == null || !previousTaskbar.Equals(taskbar))
            {
                Discover(taskbar);
                previousTaskbar = taskbar;
            }
            if (first != null && firstIsStart && Taskbar.CheckIfCentred())
            {
                int added = Volatile.Read(ref revision), removed = Volatile.Read(ref removalRevision);
                if (added != notifiedRevision || removed != notifiedRemovalRevision)
                {
                    centeredBounds.ExpectDirection(Volatile.Read(ref membershipDirection));
                    notifiedRevision = added;
                    notifiedRemovalRevision = removed;
                }
                // Read before processing structure events: the destination can be
                // exposed before those events, while the actual animation follows.
                if (ReadCentered(taskbar, out bounds, out widgets))
                {
                    if (!centeredBounds.IsMoving && (measuredRevision != Volatile.Read(ref revision)
                        || measuredRemovalRevision != Volatile.Read(ref removalRevision)
                        || Environment.TickCount64 - lastDiscovery > 2000)) Discover(taskbar);
                    return true;
                }
            }
            if (first == null || measuredRevision != Volatile.Read(ref revision)
                || Environment.TickCount64 - lastDiscovery > 2000) Discover(taskbar);
            else if (measuredRemovalRevision != Volatile.Read(ref removalRevision)) RefreshEdges(taskbar);
            if (Read(taskbar, out bounds, out widgets)) return true;
            // A removed last button can briefly return an empty rectangle before
            // Explorer delivers its structure event. Never shrink to just Start.
            RefreshEdges(taskbar);
            return Read(taskbar, out bounds, out widgets);
        }

        private bool ReadCentered(LocalPInvoke.RECT taskbar, out LocalPInvoke.RECT bounds, out LocalPInvoke.RECT widgets)
        {
            bounds = default;
            widgets = default;
            bool found = false, foundWidgets = false;
            if (first == null) return false;
            Include(ref bounds, ref found, taskbar, Rectangle(first.CurrentBoundingRectangle));
            if (!found) return false;
            double scale = Math.Max(1, LocalPInvoke.GetDpiForWindow(handle) / 96.0);
            var tray = LocalPInvoke.FindWindowExA(handle, IntPtr.Zero, "TrayNotifyWnd", null);
            LocalPInvoke.GetWindowRect(tray, out var trayBounds);
            if (RequiresNativeEdges(taskbar, bounds, trayBounds, scale))
            {
                // Explorer shifts crowded app groups left instead of centering them.
                // Read both live edges; reflecting Start around the monitor would
                // invent an edge inside the tray and reject every subsequent update.
                constrainedLayout = true;
                return false;
            }
            if (constrainedLayout)
            {
                Discover(taskbar);
                constrainedLayout = false;
                bounds = default;
                found = false;
                if (first == null) return false;
                Include(ref bounds, ref found, taskbar, Rectangle(first.CurrentBoundingRectangle));
                if (!found) return false;
            }
            bounds = centeredBounds.Follow(bounds, Environment.TickCount64, scale);
            if (widget != null) Include(ref widgets, ref foundWidgets, taskbar, Rectangle(widget.CurrentBoundingRectangle));
            return true;
        }

        private void RefreshEdges(LocalPInvoke.RECT taskbar)
        {
            // Closing an app cannot introduce a new button. Reuse the surviving
            // elements instead of stalling the animation with another tree scan.
            int removedRevision = Volatile.Read(ref removalRevision);
            var oldWidget = widget;
            var oldStart = firstIsStart ? first : null;
            first = last = widget = null;
            firstIsStart = false;
            int left = int.MaxValue, right = int.MinValue;
            for (int index = retained.Count - 1; index >= 0; index--)
            {
                var element = retained[index];
                bool visible = false;
                var bounds = default(LocalPInvoke.RECT);
                try { Include(ref bounds, ref visible, taskbar, Rectangle(element.CurrentBoundingRectangle)); }
                catch (COMException) { }
                if (!visible)
                {
                    retained.RemoveAt(index);
                    Release(element);
                    continue;
                }
                if (ReferenceEquals(element, oldWidget)) widget = element;
                else
                {
                    if (bounds.Left < left) { left = bounds.Left; first = element; firstIsStart = ReferenceEquals(element, oldStart); }
                    if (bounds.Right > right) { right = bounds.Right; last = element; }
                }
            }
            measuredRemovalRevision = removedRevision;
        }

        private bool Read(LocalPInvoke.RECT taskbar, out LocalPInvoke.RECT bounds, out LocalPInvoke.RECT widgets)
        {
            bounds = default;
            widgets = default;
            bool found = false, foundWidgets = false;
            if (first != null) Include(ref bounds, ref found, taskbar, Rectangle(first.CurrentBoundingRectangle));
            if (!found || last == null) return false;
            if (!ReferenceEquals(first, last))
            {
                var lastBounds = default(LocalPInvoke.RECT);
                bool foundLast = false;
                Include(ref lastBounds, ref foundLast, taskbar, Rectangle(last.CurrentBoundingRectangle));
                if (!foundLast) return false;
                Include(ref bounds, ref found, taskbar, lastBounds);
            }
            if (widget != null) Include(ref widgets, ref foundWidgets, taskbar, Rectangle(widget.CurrentBoundingRectangle));
            return found;
        }

        private static LocalPInvoke.RECT Rectangle(tagRECT rect) => new()
        {
            Left = rect.left, Top = rect.top, Right = rect.right, Bottom = rect.bottom
        };

        private void Discover(LocalPInvoke.RECT taskbar)
        {
            int discoveredRevision = Volatile.Read(ref revision);
            foreach (var element in retained) Release(element);
            retained.Clear();
            first = last = widget = null;
            firstIsStart = false;
            int left = int.MaxValue, right = int.MinValue;
            IUIAutomationCondition buttons = null;
            IUIAutomationElementArray elements = null;
            IUIAutomationCacheRequest request = null;
            try
            {
                buttons = ui.CreatePropertyCondition(ControlTypeProperty, ButtonControlType);
                request = ui.CreateCacheRequest();
                request.AddProperty(30011); // AutomationId
                request.AddProperty(30012); // ClassName
                request.AddProperty(30001); // BoundingRectangle
                // One provider snapshot avoids separate cross-process calls per button,
                // which can otherwise sample opposite edges from different animation frames.
                elements = this.root.FindAllBuildCache(TreeScope.TreeScope_Descendants, buttons, request);
                if (elements == null) return;
                for (var index = 0; index < elements.Length; index++)
                {
                    IUIAutomationElement element = elements.GetElement(index);
                    if (element == null) continue;
                    try
                    {
                        var id = element.CachedAutomationId;
                        var className = element.CachedClassName;
                        bool isWidget = IsWidgetsButton(id, className);
                        if (!isWidget && !IsAppListButton(id, className)) continue;
                        var rect = element.CachedBoundingRectangle;
                        var button = Rectangle(rect);
                        bool visible = false;
                        var clipped = default(LocalPInvoke.RECT);
                        Include(ref clipped, ref visible, taskbar, button);
                        if (!visible) continue;
                        retained.Add(element);
                        if (isWidget) widget = element;
                        else
                        {
                            if (button.Left < left) { left = button.Left; first = element; firstIsStart = id == "StartButton"; }
                            if (button.Right > right) { right = button.Right; last = element; }
                        }
                    }
                    finally { if (!retained.Contains(element)) Release(element); }
                }
                measuredRevision = discoveredRevision;
                measuredRemovalRevision = Volatile.Read(ref removalRevision);
                lastDiscovery = Environment.TickCount64;
                if (firstIsStart && first != null && last != null)
                {
                    var measured = Rectangle(first.CurrentBoundingRectangle);
                    measured.Right = last.CurrentBoundingRectangle.right;
                    // First and last buttons may report different frames after a
                    // group disappears. Centered layouts belong to the monitor,
                    // not that mixed snapshot. Windows rounds the right edge up.
                    centeredBounds.Initialize(measured, lastDiscovery,
                        (taskbar.Left + (double)taskbar.Right + 1) / 2);
                }
            }
            finally
            {
                Release(elements);
                Release(request);
                Release(buttons);
            }
        }

        public void Dispose()
        {
            try { ui.RemoveStructureChangedEventHandler(root, this); }
            catch (COMException) { }
            foreach (var element in retained) Release(element);
            retained.Clear();
            Release(root);
        }
    }

    internal static bool RequiresNativeEdges(LocalPInvoke.RECT taskbar, LocalPInvoke.RECT start,
        LocalPInvoke.RECT tray, double scale)
    {
        if (tray.Right <= tray.Left || tray.Left < taskbar.Left || tray.Left >= taskbar.Right) return false;
        double centeredRight = taskbar.Left + (double)taskbar.Right + 1 - start.Left;
        // Switch before the next icon reaches the tray, including Explorer's
        // intermediate layout where the group has already started shifting.
        double iconWidth = Math.Max(start.Right - (double)start.Left, Math.Ceiling(44 * scale));
        return centeredRight >= tray.Left - Math.Ceiling(8 * scale) - iconWidth;
    }

    internal static bool IsWidgetsButton(string id, string className) =>
        id == "WidgetsButton" || (className?.StartsWith("Taskbar.WidgetsButton", StringComparison.Ordinal) ?? false);

    internal static bool IsAppListButton(string id, string className) =>
        id == "StartButton" || id == "SearchButton" || id == "SearchBox" || id == "TaskViewButton"
        || id == "CopilotButton" || (id?.StartsWith("Appid:", StringComparison.Ordinal) ?? false)
        || (className?.StartsWith("Taskbar.TaskList", StringComparison.Ordinal) ?? false);

    internal static void Include(ref LocalPInvoke.RECT bounds, ref bool found,
        LocalPInvoke.RECT taskbar, LocalPInvoke.RECT icon)
    {
        if (icon.Right <= icon.Left || icon.Bottom <= icon.Top || icon.Bottom <= taskbar.Top
            || icon.Top >= taskbar.Bottom || icon.Right <= taskbar.Left || icon.Left >= taskbar.Right) return;
        icon.Left = Math.Max(icon.Left, taskbar.Left);
        icon.Right = Math.Min(icon.Right, taskbar.Right);
        icon.Top = Math.Max(icon.Top, taskbar.Top);
        icon.Bottom = Math.Min(icon.Bottom, taskbar.Bottom);
        if (!found) { bounds = icon; found = true; return; }
        bounds.Left = Math.Min(bounds.Left, icon.Left);
        bounds.Right = Math.Max(bounds.Right, icon.Right);
        bounds.Top = Math.Min(bounds.Top, icon.Top);
        bounds.Bottom = Math.Max(bounds.Bottom, icon.Bottom);
    }

    internal static LocalPInvoke.RECT GetRegion(Types.Taskbar taskbar, Types.SegmentSettings settings)
    {
        // UIA reports screen coordinates. SetWindowRgn needs coordinates relative to the taskbar.
        // Leave eight logical pixels of padding around the full icon button bounds.
        var padding = (int)Math.Ceiling(8 * taskbar.ScaleFactor);
        var width = taskbar.TaskbarRect.Right - taskbar.TaskbarRect.Left;
        return new LocalPInvoke.RECT
        {
            Left = Math.Max(0, taskbar.AppListRect.Left - taskbar.TaskbarRect.Left - padding
                + NativeGeometry.Pixel(settings.MarginLeft * taskbar.ScaleFactor)),
            Right = Math.Min(width, taskbar.AppListRect.Right - taskbar.TaskbarRect.Left + padding
                - NativeGeometry.Pixel(settings.MarginRight * taskbar.ScaleFactor)) + 1,
            Top = NativeGeometry.Pixel(settings.MarginTop * taskbar.ScaleFactor),
            Bottom = taskbar.TaskbarRect.Bottom - taskbar.TaskbarRect.Top
                - NativeGeometry.Pixel(settings.MarginBottom * taskbar.ScaleFactor) + 1
        };
    }

    private static void Release(object value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
}
