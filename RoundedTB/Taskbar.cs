using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using Newtonsoft.Json;
using System.Runtime.InteropServices;



namespace RoundedTB
{
    class Taskbar
    {
        /// <summary>
        /// Checks if the taskbar is centred.
        /// </summary>
        /// <returns>
        /// A bool indicating if the taskbar is centred.
        /// </returns>
        public static bool CheckIfCentred()
        {
            bool retVal;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced"))
                {
                    if (key != null)
                    {
                        int val = (int)key.GetValue("TaskbarAl");

                        if (val == 1)
                        {
                            retVal = true;
                        }
                        else
                        {
                            retVal = false;
                        }
                    }
                    else
                    {
                        retVal = false;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
            return retVal;
        }

        /// <summary>
        /// Compares two taskbars' rects to see if they've changed
        /// </summary>
        /// <returns>
        /// a bool indicating if the taskbar's, applist's, and tray's rects rects have changed.
        /// </returns>
        public static bool TaskbarRefreshRequired(Types.Taskbar currentTB, Types.Taskbar newTB, bool isDynamic)
        {
            // REMINDER: newTB will only have rect & hwnd info. Everything else will be null.


            bool taskbarRectChanged = true;
            bool appListRectChanged = true;
            bool trayRectChanged = true;

            if (
                currentTB.TaskbarRect.Left == newTB.TaskbarRect.Left &&
                currentTB.TaskbarRect.Top == newTB.TaskbarRect.Top &&
                currentTB.TaskbarRect.Right == newTB.TaskbarRect.Right &&
                currentTB.TaskbarRect.Bottom == newTB.TaskbarRect.Bottom)
            {
                taskbarRectChanged = false;
            }
            if (
                currentTB.AppListRect.Left == newTB.AppListRect.Left &&
                currentTB.AppListRect.Top == newTB.AppListRect.Top &&
                currentTB.AppListRect.Right == newTB.AppListRect.Right &&
                currentTB.AppListRect.Bottom == newTB.AppListRect.Bottom)
            {
                appListRectChanged = false;
            }
            if (
                (currentTB.TrayRect.Left + 5 >= newTB.TrayRect.Left && currentTB.TrayRect.Left - 5 <= newTB.TrayRect.Left) &&
                currentTB.TrayRect.Top == newTB.TrayRect.Top &&
                currentTB.TrayRect.Right == newTB.TrayRect.Right &&
                currentTB.TrayRect.Bottom == newTB.TrayRect.Bottom)
            {
                trayRectChanged = false;
            }

            if (isDynamic && (taskbarRectChanged || appListRectChanged || trayRectChanged
                || currentTB.HasMeasuredAppListBounds != newTB.HasMeasuredAppListBounds
                || !currentTB.WidgetsRect.Equals(newTB.WidgetsRect)))
            {
                return true;
            }
            else if (!isDynamic && taskbarRectChanged)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the rects of the three components of the taskbar from their respective handles.
        /// </summary>
        /// <returns>
        /// a partial Taskbar containing just rects and handles.
        /// </returns>
        public static Types.Taskbar GetQuickTaskbarRects(IntPtr taskbarHwnd, IntPtr trayHwnd, IntPtr appListHwnd, bool measureAppList = false)
        {
            LocalPInvoke.GetWindowRect(taskbarHwnd, out LocalPInvoke.RECT taskbarRectCheck);
            LocalPInvoke.GetWindowRect(trayHwnd, out LocalPInvoke.RECT trayRectCheck);
            LocalPInvoke.GetWindowRect(appListHwnd, out LocalPInvoke.RECT appListRectCheck);

            LocalPInvoke.RECT icons = default;
            LocalPInvoke.RECT widgets = default;
            var measured = measureAppList && TaskbarIconBounds.TryGet(taskbarHwnd, taskbarRectCheck, out icons, out widgets);
            if (measured) appListRectCheck = icons;
            return new Types.Taskbar()
            {
                WidgetsRect = widgets,
                HasMeasuredAppListBounds = measured,
                TaskbarHwnd = taskbarHwnd,
                TrayHwnd = trayHwnd,
                AppListHwnd = appListHwnd,
                TaskbarRect = taskbarRectCheck,
                TrayRect = trayRectCheck,
                AppListRect = appListRectCheck
            };
        }

        /// <summary>
        /// Resets the specified taskbar.
        /// </summary>
        public static void ResetTaskbar(Types.Taskbar taskbar, Types.Settings settings, bool removeLayeredStyle = true)
        {
            LocalPInvoke.SetWindowRgn(taskbar.TaskbarHwnd, IntPtr.Zero, true);
            LocalPInvoke.SetLayeredWindowAttributes(taskbar.TaskbarHwnd, 0, 255, LocalPInvoke.LWA_ALPHA);
            int style = LocalPInvoke.GetWindowLong(taskbar.TaskbarHwnd, LocalPInvoke.GWL_EXSTYLE).ToInt32();
            if (removeLayeredStyle && (style & LocalPInvoke.WS_EX_LAYERED) == LocalPInvoke.WS_EX_LAYERED)
            {
                LocalPInvoke.SetWindowLong(taskbar.TaskbarHwnd, LocalPInvoke.GWL_EXSTYLE, LocalPInvoke.GetWindowLong(taskbar.TaskbarHwnd, LocalPInvoke.GWL_EXSTYLE).ToInt32() ^ LocalPInvoke.WS_EX_LAYERED);
            }
            style = LocalPInvoke.GetWindowLong(taskbar.TaskbarHwnd, LocalPInvoke.GWL_EXSTYLE).ToInt32();
            if ((style & LocalPInvoke.WS_EX_TRANSPARENT) == LocalPInvoke.WS_EX_TRANSPARENT)
            {
                LocalPInvoke.SetWindowLong(taskbar.TaskbarHwnd, LocalPInvoke.GWL_EXSTYLE, LocalPInvoke.GetWindowLong(taskbar.TaskbarHwnd, LocalPInvoke.GWL_EXSTYLE).ToInt32() ^ LocalPInvoke.WS_EX_TRANSPARENT);
            }

        }

        /// <summary>
        /// Creates a basic region for a specific taskbar and applies it.
        /// </summary>
        /// <returns>
        /// a bool indicating success.
        /// </returns>
        public static bool UpdateSimpleTaskbar(Types.Taskbar taskbar, Types.Settings settings)
        {
            try
            {
                EnsureLayered(taskbar);
                // Create an effective region to be applied to the taskbar
                Types.EffectiveRegion taskbarEffectiveRegion = new Types.EffectiveRegion
                {
                    CornerRadius = NativeGeometry.Pixel(settings.SimpleTaskbarLayout.CornerRadius * taskbar.ScaleFactor),
                    Top = NativeGeometry.Pixel(settings.SimpleTaskbarLayout.MarginTop * taskbar.ScaleFactor),
                    Left = NativeGeometry.Pixel(settings.SimpleTaskbarLayout.MarginLeft * taskbar.ScaleFactor),
                    Width = NativeGeometry.Pixel(taskbar.TaskbarRect.Right - taskbar.TaskbarRect.Left - (settings.SimpleTaskbarLayout.MarginRight * taskbar.ScaleFactor)) + 1,
                    Height = NativeGeometry.Pixel(taskbar.TaskbarRect.Bottom - taskbar.TaskbarRect.Top - (settings.SimpleTaskbarLayout.MarginBottom * taskbar.ScaleFactor)) + 1
                };

                IntPtr region = NativeGeometry.CreateRegion(taskbarEffectiveRegion.Left, taskbarEffectiveRegion.Top, taskbarEffectiveRegion.Width, taskbarEffectiveRegion.Height, taskbarEffectiveRegion.CornerRadius, taskbarEffectiveRegion.CornerRadius);
                if (!SegmentHover.ShowApps(taskbar, settings))
                {
                    LocalPInvoke.DeleteObject(region);
                    region = LocalPInvoke.CreateRectRgn(0, 0, 0, 0);
                }
                if (!taskbar.RevealAllSegments) AnimateHoverRegion(region, taskbar.AppHoverTransition, settings.SimpleTaskbarLayout);
                LocalPInvoke.SetWindowRgn(taskbar.TaskbarHwnd, region, true);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool IsFullscreenEdge(LocalPInvoke.RECT bar, LocalPInvoke.RECT monitor)
            => bar.Right > bar.Left && bar.Bottom >= monitor.Bottom
                && bar.Top >= monitor.Bottom - 2;

        internal static bool HideFullscreenEdge(Types.Taskbar taskbar, bool revealShellUi)
        {
            var monitor = new MonitorStuff.MONITORINFO { cbSize = (uint)Marshal.SizeOf<MonitorStuff.MONITORINFO>() };
            if (!LocalPInvoke.GetWindowRect(taskbar.TaskbarHwnd, out var native)
                || !MonitorStuff.GetMonitorInfo(LocalPInvoke.MonitorFromWindow(taskbar.TaskbarHwnd, 2), ref monitor)) return false;
            bool collapsedEdge = IsFullscreenEdge(native, monitor.rcMonitor);
            bool foregroundFullscreen = FullscreenWindowBounds.ForegroundIsFullscreen(taskbar.TaskbarHwnd, monitor.rcMonitor);
            if (!collapsedEdge && !foregroundFullscreen) return false;
            if (revealShellUi)
            {
                if (!collapsedEdge) return false;
                // Shell selection UI can leave Explorer at its fullscreen activation edge.
                // Restore the full height before measuring and applying segment regions.
                long measuredHeight = Math.Max((long)native.Bottom - native.Top,
                    (long)taskbar.TaskbarRect.Bottom - taskbar.TaskbarRect.Top);
                int height = (int)Math.Clamp(measuredHeight > 2 ? measuredHeight
                    : Math.Round(48 * Math.Max(1, taskbar.ScaleFactor)), 3, int.MaxValue);
                LocalPInvoke.SetWindowPos(taskbar.TaskbarHwnd, IntPtr.Zero, native.Left,
                    monitor.rcMonitor.Bottom - height, native.Right - native.Left, height,
                    LocalPInvoke.SetWindowPosFlags.IgnoreZOrder | LocalPInvoke.SetWindowPosFlags.DoNotActivate);
                taskbar.Ignored = true;
                return false;
            }
            EnsureLayered(taskbar);
            LocalPInvoke.SetLayeredWindowAttributes(taskbar.TaskbarHwnd, 0, 0, LocalPInvoke.LWA_ALPHA);
            var empty = LocalPInvoke.CreateRectRgn(0, 0, 0, 0);
            if (LocalPInvoke.SetWindowRgn(taskbar.TaskbarHwnd, empty, true) == 0)
                LocalPInvoke.DeleteObject(empty);
            // Fullscreen suppression replaces even a previously restored region.
            // The next shell selection must reapply its full-bar appearance.
            taskbar.AppearanceRestored = false;
            taskbar.TaskbarHidden = true;
            return true;
        }

        internal static void EnsureVisible(Types.Taskbar taskbar)
        {
            EnsureLayered(taskbar);
            uint style = LocalPInvoke.GetWindowStyleBits(taskbar.TaskbarHwnd, LocalPInvoke.GWL_EXSTYLE);
            if ((style & LocalPInvoke.WS_EX_TRANSPARENT) != 0)
                LocalPInvoke.SetWindowLong(taskbar.TaskbarHwnd, LocalPInvoke.GWL_EXSTYLE,
                    unchecked((int)(style & ~(uint)LocalPInvoke.WS_EX_TRANSPARENT)));
            LocalPInvoke.GetLayeredWindowAttributes(taskbar.TaskbarHwnd, out _, out byte opacity, out _);
            if (opacity != 255) LocalPInvoke.SetLayeredWindowAttributes(taskbar.TaskbarHwnd, 0, 255, LocalPInvoke.LWA_ALPHA);
            if (taskbar.TaskbarHidden) taskbar.Ignored = true;
            taskbar.TaskbarHidden = false;
        }

        /// <summary>
        /// Creates a dynamic region for a specific taskbar and applies it.
        /// </summary>
        /// <returns>
        /// a bool indicating success.
        /// </returns>
        public static bool UpdateDynamicTaskbar(Types.Taskbar taskbar, Types.Settings settings)
        {
            // Never crop a modern taskbar using the legacy proxy window's incomplete bounds.
            if (settings.IsWindows11 && !taskbar.HasMeasuredAppListBounds)
                return UpdateSimpleTaskbar(taskbar, settings);
            IntPtr mainRegion = IntPtr.Zero;
            try
            {
                EnsureLayered(taskbar);
                int centredDistanceFromEdge = 0;

                // Create an effective region to be applied to the taskbar for the applist
                Types.EffectiveRegion taskbarEffectiveRegion = new Types.EffectiveRegion
                {
                    CornerRadius = NativeGeometry.Pixel(settings.DynamicAppListLayout.CornerRadius * taskbar.ScaleFactor),
                    Top = NativeGeometry.Pixel(settings.DynamicAppListLayout.MarginTop * taskbar.ScaleFactor),
                    Left = NativeGeometry.Pixel(settings.DynamicAppListLayout.MarginLeft * taskbar.ScaleFactor),
                    Width = NativeGeometry.Pixel(taskbar.TaskbarRect.Right - taskbar.TaskbarRect.Left - (settings.DynamicAppListLayout.MarginRight * taskbar.ScaleFactor)) + 1,
                    Height = NativeGeometry.Pixel(taskbar.TaskbarRect.Bottom - taskbar.TaskbarRect.Top - (settings.DynamicAppListLayout.MarginBottom * taskbar.ScaleFactor)) + 1
                };

                // Create an effective region to be applied to the taskbar for the applist
                Types.EffectiveRegion centredEffectiveRegion = new Types.EffectiveRegion
                {
                    CornerRadius = NativeGeometry.Pixel(settings.DynamicAppListLayout.CornerRadius * taskbar.ScaleFactor),
                    Top = NativeGeometry.Pixel(settings.DynamicAppListLayout.MarginTop * taskbar.ScaleFactor),
                    Left = NativeGeometry.Pixel(settings.DynamicAppListLayout.MarginRight * taskbar.ScaleFactor) - 1,
                    Width = NativeGeometry.Pixel(taskbar.TaskbarRect.Right - taskbar.TaskbarRect.Left - (settings.DynamicAppListLayout.MarginRight * taskbar.ScaleFactor)) + 1,
                    Height = NativeGeometry.Pixel(taskbar.TaskbarRect.Bottom - taskbar.TaskbarRect.Top - (settings.DynamicAppListLayout.MarginBottom * taskbar.ScaleFactor)) + 1
                };

                // Create an effective region to be applied to the taskbar for the tray
                Types.EffectiveRegion trayEffectiveRegion = new Types.EffectiveRegion
                {
                    CornerRadius = NativeGeometry.Pixel(settings.DynamicTrayLayout.CornerRadius * taskbar.ScaleFactor),
                    Top = NativeGeometry.Pixel(settings.DynamicTrayLayout.MarginTop * taskbar.ScaleFactor),
                    Left = NativeGeometry.Pixel((settings.DynamicTrayLayout.MarginLeft * taskbar.ScaleFactor) - (3 * taskbar.ScaleFactor)), // Add extra margin for taskbar left as there's no "padding" provided by Windows and always looks weird as soon as you trim it otherwise.
                    Width = NativeGeometry.Pixel(taskbar.TaskbarRect.Right - taskbar.TaskbarRect.Left - (settings.DynamicTrayLayout.MarginRight * taskbar.ScaleFactor)) + 1,
                    Height = NativeGeometry.Pixel(taskbar.TaskbarRect.Bottom - taskbar.TaskbarRect.Top - (settings.DynamicTrayLayout.MarginBottom * taskbar.ScaleFactor)) + 1
                };

                Types.EffectiveRegion widgetsEffectiveRegion = new Types.EffectiveRegion
                {
                    CornerRadius = NativeGeometry.Pixel(settings.DynamicWidgetsLayout.CornerRadius * taskbar.ScaleFactor),
                    Top = NativeGeometry.Pixel(settings.DynamicWidgetsLayout.MarginTop * taskbar.ScaleFactor),
                    Left = NativeGeometry.Pixel(settings.DynamicWidgetsLayout.MarginLeft * taskbar.ScaleFactor),
                    Width = NativeGeometry.Pixel(168 * taskbar.ScaleFactor - (settings.DynamicWidgetsLayout.MarginRight * taskbar.ScaleFactor)) + 1,
                    Height = NativeGeometry.Pixel(taskbar.TaskbarRect.Bottom - taskbar.TaskbarRect.Top - (settings.DynamicWidgetsLayout.MarginBottom * taskbar.ScaleFactor)) + 1
                };

                centredDistanceFromEdge = taskbar.TaskbarRect.Right - taskbar.AppListRect.Right - NativeGeometry.Pixel(2 * taskbar.ScaleFactor);

                // If on Windows 10, add an extra 20 logical pixels for the grabhandle
                if (!settings.IsWindows11)
                {
                    centredDistanceFromEdge -= NativeGeometry.Pixel(20 * taskbar.ScaleFactor);
                }

                // Create region for if the taskbar is centred by take the right-to-right distance (centredDistanceFromEdge) off from both sides, as well as the margin
                if (settings.IsWindows11 && taskbar.HasMeasuredAppListBounds)
                {
                    var bounds = TaskbarIconBounds.GetRegion(taskbar, settings.DynamicAppListLayout);
                    mainRegion = NativeGeometry.CreateRegion(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom,
                        taskbarEffectiveRegion.CornerRadius, taskbarEffectiveRegion.CornerRadius);
                }
                else if (settings.IsCentred)
                {
                    mainRegion = NativeGeometry.CreateRegion(
                        centredDistanceFromEdge + centredEffectiveRegion.Left,
                        centredEffectiveRegion.Top,
                        centredEffectiveRegion.Width - centredDistanceFromEdge,
                        centredEffectiveRegion.Height,
                        centredEffectiveRegion.CornerRadius,
                        centredEffectiveRegion.CornerRadius
                        );
                }

                // Create a region for if the taskbar is left-aligned, right-to-right distance (centredDistanceFromEdge) off from the right-hand side, as well as the margin
                else
                {

                    mainRegion = NativeGeometry.CreateRegion(
                        taskbarEffectiveRegion.Left,
                        taskbarEffectiveRegion.Top,
                        taskbarEffectiveRegion.Width - centredDistanceFromEdge,
                        taskbarEffectiveRegion.Height,
                        taskbarEffectiveRegion.CornerRadius,
                        taskbarEffectiveRegion.CornerRadius
                        );
                }

                if (!SegmentHover.ShowApps(taskbar, settings))
                {
                    LocalPInvoke.DeleteObject(mainRegion);
                    mainRegion = LocalPInvoke.CreateRectRgn(0, 0, 0, 0);
                }

                if (!taskbar.RevealAllSegments) AnimateHoverRegion(mainRegion, taskbar.AppHoverTransition, settings.DynamicAppListLayout);

                // If the user has it enabled and the tray handle isn't null, create a region for the system tray and merge it with the taskbar region
                if (SegmentHover.ShowTray(taskbar, settings) && taskbar.TrayHwnd != IntPtr.Zero)
                {
                    IntPtr trayRegion = NativeGeometry.CreateRegion(
                        (taskbar.TrayRect.Left - taskbar.TaskbarRect.Left) - trayEffectiveRegion.Left,
                        trayEffectiveRegion.Top,
                        trayEffectiveRegion.Width,
                        trayEffectiveRegion.Height,
                        trayEffectiveRegion.CornerRadius,
                        trayEffectiveRegion.CornerRadius
                        );

                    if (!taskbar.RevealAllSegments) AnimateHoverRegion(trayRegion, taskbar.TrayHoverTransition, settings.DynamicTrayLayout);
                    LocalPInvoke.CombineRgn(mainRegion, mainRegion, trayRegion, 2);
                    LocalPInvoke.DeleteObject(trayRegion);
                }

                if (SegmentHover.ShowWidgets(taskbar, settings) && taskbar.HasWidgets)
                {
                    IntPtr widgetsRegion = NativeGeometry.CreateRegion(
                        widgetsEffectiveRegion.Left,
                        widgetsEffectiveRegion.Top,
                        widgetsEffectiveRegion.Width,
                        widgetsEffectiveRegion.Height,
                        widgetsEffectiveRegion.CornerRadius,
                        widgetsEffectiveRegion.CornerRadius
                        );

                    if (!taskbar.RevealAllSegments) AnimateHoverRegion(widgetsRegion, taskbar.WidgetsHoverTransition, settings.DynamicWidgetsLayout);
                    LocalPInvoke.CombineRgn(mainRegion, mainRegion, widgetsRegion, 2);
                    LocalPInvoke.DeleteObject(widgetsRegion);
                }

                AddActivationPill(mainRegion, taskbar.AppListRect, taskbar, settings.DynamicAppListLayout, taskbar.AppHoverTransition);
                if (taskbar.TrayHwnd != IntPtr.Zero)
                    AddActivationPill(mainRegion, taskbar.TrayRect, taskbar, settings.DynamicTrayLayout, taskbar.TrayHoverTransition);

                // Apply the final region to the taskbar
                if (mainRegion == IntPtr.Zero || LocalPInvoke.SetWindowRgn(taskbar.TaskbarHwnd, mainRegion, true) == 0)
                    return false;
                // Windows owns the region after a successful SetWindowRgn.
                mainRegion = IntPtr.Zero;

                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (mainRegion != IntPtr.Zero) LocalPInvoke.DeleteObject(mainRegion);
            }
        }

        /// <summary>
        /// Checks if there are any new taskbars, or if any taskbars are no longer present.
        /// </summary>
        /// <returns>
        /// a bool indicating success.
        /// </returns>
        public static bool TaskbarCountOrHandleChanged(int taskbarCount, IntPtr mainTaskbarHandle)
        {
            List<IntPtr> currentTaskbars = new List<IntPtr>();
            bool otherTaskbarsExist = true;
            IntPtr hwndPrevious = IntPtr.Zero;
            currentTaskbars.Add(LocalPInvoke.FindWindowExA(IntPtr.Zero, hwndPrevious, "Shell_TrayWnd", null));

            if (currentTaskbars[0] == IntPtr.Zero)
            {
                return false;
            }

            if (currentTaskbars[0] != mainTaskbarHandle)
            {
                return true;
            }

            while (otherTaskbarsExist)
            {
                IntPtr hwndCurrent = LocalPInvoke.FindWindowExA(IntPtr.Zero, hwndPrevious, "Shell_SecondaryTrayWnd", null);
                hwndPrevious = hwndCurrent;

                if (hwndCurrent == IntPtr.Zero)
                {
                    otherTaskbarsExist = false;
                }
                else
                {
                    currentTaskbars.Add(hwndCurrent);
                }
            }
            if (currentTaskbars.Count != taskbarCount)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if the provided update is valid.
        /// </summary>
        /// <returns>
        /// A bool indicating if the update is valid.
        /// </returns>
        public static bool CheckDynamicUpdateIsValid(Types.Taskbar currentTB, Types.Taskbar newTB)
        {
            // REMINDER: newTB will only have rect & hwnd info. Everything else will be null.

            // Check if either of the supplied taskbars are null
            if (currentTB == null || newTB == null)
            {
                return false;
            }

            // Check if the taskbar handles are different
            if (currentTB.TaskbarHwnd != newTB.TaskbarHwnd)
            {
                return false;
            }

            // Get width of app list. Not strictly necessary as the applist is always measured from the left but doing so just in case
            int newAppListWidth = newTB.AppListRect.Right - newTB.AppListRect.Left;
            int currentAppListWidth = currentTB.AppListRect.Right - currentTB.AppListRect.Left;

            if (newTB.AppListRect.Right > newTB.TrayRect.Left && newTB.TrayRect.Left != 0)
            {
                return false;
            }

            if (newAppListWidth == newTB.TrayRect.Left && newTB.TrayRect.Left != 0)
            {
                return false;
            }

            if (newAppListWidth <= 20 * currentTB.ScaleFactor && newAppListWidth != 0)
            {
                return false;
            }

            if (newAppListWidth >= newTB.TaskbarRect.Right - newTB.TaskbarRect.Left && newAppListWidth != 0)
            {
                return false;
            }

            Debug.WriteLine($"Old width: {currentAppListWidth}\nNew width: {newAppListWidth}");
            return true;
        }

        /// <summary>
        /// Collects information on any currently-present taskbars.
        /// </summary>
        /// <returns>
        /// A list of taskbars populated with information about their size, handles etc.
        /// </returns>
        public static List<Types.Taskbar> GenerateTaskbarInfo(bool measureAppList = false, bool enableEffects = true)
        {
            List<Types.Taskbar> retVal = new List<Types.Taskbar>();

            IntPtr hwndMain = LocalPInvoke.FindWindowExA(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null); // Find main taskbar
            LocalPInvoke.GetWindowRect(hwndMain, out LocalPInvoke.RECT rectMain); // Get the RECT of the main taskbar
            IntPtr hrgnMain = IntPtr.Zero; // Set recovery region to IntPtr.Zero
            IntPtr hwndTray = LocalPInvoke.FindWindowExA(hwndMain, IntPtr.Zero, "TrayNotifyWnd", null); // Get handle to the main taskbar's tray
            LocalPInvoke.GetWindowRect(hwndTray, out LocalPInvoke.RECT rectTray); // Get the RECT for the main taskbar's tray
            IntPtr hwndAppList = LocalPInvoke.FindWindowExA(LocalPInvoke.FindWindowExA(hwndMain, IntPtr.Zero, "ReBarWindow32", null), IntPtr.Zero, "MSTaskSwWClass", null); // Get the handle to the main taskbar's app list
            LocalPInvoke.GetWindowRect(hwndAppList, out LocalPInvoke.RECT rectAppList);// Get the RECT for the main taskbar's app list

            retVal.Add(new Types.Taskbar
            {
                TaskbarHwnd = hwndMain,
                TrayHwnd = hwndTray,
                AppListHwnd = hwndAppList,
                TaskbarRect = rectMain,
                TrayRect = rectTray,
                AppListRect = rectAppList,
                RecoveryHrgn = hrgnMain,
                ScaleFactor = Convert.ToDouble(LocalPInvoke.GetDpiForWindow(hwndMain)) / 96.00,
                TaskbarRes = $"{rectMain.Right - rectMain.Left} x {rectMain.Bottom - rectMain.Top}",
                Ignored = false
            });
            int style = LocalPInvoke.GetWindowLong(hwndMain, LocalPInvoke.GWL_EXSTYLE).ToInt32();
            if (enableEffects && (style & LocalPInvoke.WS_EX_LAYERED) != LocalPInvoke.WS_EX_LAYERED)
            {
                LocalPInvoke.SetWindowLong(hwndMain, LocalPInvoke.GWL_EXSTYLE, LocalPInvoke.GetWindowLong(hwndMain, LocalPInvoke.GWL_EXSTYLE).ToInt32() ^ LocalPInvoke.WS_EX_LAYERED);
                LocalPInvoke.SetLayeredWindowAttributes(hwndMain, 0, 255, LocalPInvoke.LWA_ALPHA);
            }




            bool i = true;
            IntPtr hwndPrevious = IntPtr.Zero;
            while (i)
            {
                IntPtr hwndCurrent = LocalPInvoke.FindWindowExA(IntPtr.Zero, hwndPrevious, "Shell_SecondaryTrayWnd", null);
                hwndPrevious = hwndCurrent;

                if (hwndCurrent == IntPtr.Zero)
                {
                    i = false;
                }
                else
                {
                    LocalPInvoke.GetWindowRect(hwndCurrent, out LocalPInvoke.RECT rectCurrent);
                    IntPtr hrgnCurrent = IntPtr.Zero; // Recovery restores the native, unclipped taskbar.
                    Interaction interaction = new Interaction(App.Main);
                    IntPtr hwndSecTray = IntPtr.Zero;
                    if (interaction.IsWindows11())
                    {
                        IntPtr imd = LocalPInvoke.FindWindowExA(hwndCurrent, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null);
                        hwndSecTray = LocalPInvoke.FindWindowExA(hwndCurrent, imd, "Windows.UI.Composition.DesktopWindowContentBridge", null);
                    }
                    else
                    {
                        hwndSecTray = LocalPInvoke.FindWindowExA(hwndCurrent, IntPtr.Zero, "TrayNotifyWnd", null); // Get handle to this secondary taskbar's tray
                    }
                    LocalPInvoke.GetWindowRect(hwndSecTray, out LocalPInvoke.RECT rectSecTray); // Get the RECT for this secondary taskbar's tray
                    IntPtr hwndSecAppList = LocalPInvoke.FindWindowExA(LocalPInvoke.FindWindowExA(hwndCurrent, IntPtr.Zero, "WorkerW", null), IntPtr.Zero, "MSTaskListWClass", null); // Get the handle to the main taskbar's app list
                    LocalPInvoke.GetWindowRect(hwndSecAppList, out LocalPInvoke.RECT rectSecAppList);// Get the RECT for this secondary taskbar's app list
                    retVal.Add(new Types.Taskbar
                    {
                        TaskbarHwnd = hwndCurrent,
                        TrayHwnd = hwndSecTray,
                        AppListHwnd = hwndSecAppList,
                        TaskbarRect = rectCurrent,
                        TrayRect = rectSecTray,
                        AppListRect = rectSecAppList,
                        RecoveryHrgn = hrgnCurrent,
                        ScaleFactor = Convert.ToDouble(LocalPInvoke.GetDpiForWindow(hwndCurrent)) / 96.00,
                        TaskbarRes = $"{rectCurrent.Right - rectCurrent.Left} x {rectCurrent.Bottom - rectCurrent.Top}",
                        Ignored = false
                    });
                    style = LocalPInvoke.GetWindowLong(hwndCurrent, LocalPInvoke.GWL_EXSTYLE).ToInt32();
                    if (enableEffects && (style & LocalPInvoke.WS_EX_LAYERED) != LocalPInvoke.WS_EX_LAYERED)
                    {
                        LocalPInvoke.SetWindowLong(hwndCurrent, LocalPInvoke.GWL_EXSTYLE, LocalPInvoke.GetWindowLong(hwndCurrent, LocalPInvoke.GWL_EXSTYLE).ToInt32() ^ LocalPInvoke.WS_EX_LAYERED);
                        LocalPInvoke.SetLayeredWindowAttributes(hwndCurrent, 0, 255, LocalPInvoke.LWA_ALPHA);
                    }
                }
            }

            //foreach (var tb in retVal)
            //{
            //    TaskbarShouldBeFilled(tb.TaskbarHwnd);
            //}
            if (measureAppList)
            {
                foreach (var taskbar in retVal)
                {
                    if (TaskbarIconBounds.TryGet(taskbar.TaskbarHwnd, taskbar.TaskbarRect, out var icons, out var widgets))
                    {
                        taskbar.WidgetsRect = widgets;
                        taskbar.AppListRect = icons;
                        taskbar.HasMeasuredAppListBounds = true;
                    }
                }
            }
            return retVal;
        }

        /// <summary>
        /// Checks if the given taskbar should be filled to the edge of the screen.
        /// </summary>
        /// <returns>
        /// A bool indicating whether or not the taskbar needs to be filled.
        /// </returns>
        private static IntPtr startForeground;
        private static uint startProcessId;
        private static bool foregroundIsStart;

        internal static bool IsStartMenuProcess(string name)
            => string.Equals(name, "StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "SearchHost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "SearchApp", StringComparison.OrdinalIgnoreCase);

        internal static bool IsStartMenuOpenOnMonitor(IntPtr taskbar)
        {
            var foreground = LocalPInvoke.GetForegroundWindow();
            if (foreground == IntPtr.Zero || !LocalPInvoke.IsWindowVisible(foreground)) return false;
            LocalPInvoke.GetWindowThreadProcessId(foreground, out var pid);
            // Resolve identity only when focus changes, not on every animation frame.
            if (foreground != startForeground || pid != startProcessId)
            {
                startForeground = foreground;
                startProcessId = pid;
                foregroundIsStart = false;
                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    foregroundIsStart = IsStartMenuProcess(process.ProcessName);
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }
            return foregroundIsStart
                && LocalPInvoke.MonitorFromWindow(foreground, 2) == LocalPInvoke.MonitorFromWindow(taskbar, 2)
                && LocalPInvoke.DwmGetWindowAttribute(foreground, LocalPInvoke.DWMWINDOWATTRIBUTE.Cloaked,
                    out bool cloaked, 4) == 0 && !cloaked;
        }

        internal static bool IsTrayPopupHost(string className, string processName)
            => (processName.Equals("explorer", StringComparison.OrdinalIgnoreCase)
                && className is "TopLevelWindowForOverflowXamlIsland" or "NotifyIconOverflowWindow" or "Shell_InputSwitchTopLevelWindow")
                || (processName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
                    && className == "Windows.UI.Core.CoreWindow")
                || (processName.Equals("ShellHost", StringComparison.OrdinalIgnoreCase)
                    && className == "ControlCenterWindow");

        private static readonly Dictionary<IntPtr, uint> openTrayPopups = new();
        private static IntPtr trayPopupForeground;
        private static uint trayPopupProcess;
        private static bool foregroundIsTrayPopup;

        internal static bool IsTrayPopupOpenOnMonitor(IntPtr taskbar)
        {
            // Keyboard layout opens without taking foreground focus. Discover
            // its dedicated host directly, then track its visible lifetime.
            var input = IntPtr.Zero;
            while ((input = LocalPInvoke.FindWindowEx(IntPtr.Zero, input, "Shell_InputSwitchTopLevelWindow", null)) != IntPtr.Zero)
            {
                if (!LocalPInvoke.IsWindowVisible(input)) continue;
                LocalPInvoke.GetWindowThreadProcessId(input, out var owner);
                if (IsExplorerPreviewProcess(owner)) openTrayPopups[input] = owner;
            }
            var foreground = LocalPInvoke.GetForegroundWindow();
            LocalPInvoke.GetWindowThreadProcessId(foreground, out var pid);
            if (foreground != trayPopupForeground || pid != trayPopupProcess)
            {
                trayPopupForeground = foreground;
                trayPopupProcess = pid;
                foregroundIsTrayPopup = false;
                var name = new StringBuilder(256);
                LocalPInvoke.GetClassName(foreground, name, name.Capacity);
                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    foregroundIsTrayPopup = IsTrayPopupHost(name.ToString(), process.ProcessName);
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }
            if (foregroundIsTrayPopup)
            {
                if (openTrayPopups.Count > 32) openTrayPopups.Clear();
                openTrayPopups[foreground] = pid;
            }
            // A context menu inside the panel can take focus while the panel
            // remains open. Follow the panel's lifetime, not foreground alone.
            foreach (var popup in openTrayPopups)
            {
                LocalPInvoke.GetWindowThreadProcessId(popup.Key, out var owner);
                if (owner != popup.Value || !LocalPInvoke.IsWindowVisible(popup.Key)
                    || LocalPInvoke.DwmGetWindowAttribute(popup.Key, LocalPInvoke.DWMWINDOWATTRIBUTE.Cloaked,
                        out bool cloaked, 4) != 0 || cloaked)
                {
                    openTrayPopups.Remove(popup.Key);
                    continue;
                }
                if (LocalPInvoke.MonitorFromWindow(popup.Key, 2) == LocalPInvoke.MonitorFromWindow(taskbar, 2)) return true;
            }
            return false;
        }

        private static void AddActivationPill(IntPtr region, LocalPInvoke.RECT segment, Types.Taskbar taskbar, Types.SegmentSettings settings, HoverTransition transition)
        {
            if (settings?.ShowOnHover != true || !settings.HoverActivationPill) return;
            if (transition.Initialized && transition.Progress >= 1) return;
            var bounds = SegmentHover.ActivationPill(segment, taskbar, settings);
            if (bounds.Right <= bounds.Left) return;
            var left = bounds.Left - taskbar.TaskbarRect.Left;
            var top = bounds.Top - taskbar.TaskbarRect.Top;
            var right = bounds.Right - taskbar.TaskbarRect.Left;
            var bottom = bounds.Bottom - taskbar.TaskbarRect.Top;
            var diameter = bottom - top;
            // Extend the rounded rectangle below the screen edge: only its top
            // corners remain visible, with straight sides and square bottom corners.
            var pill = NativeGeometry.CreateRegion(left, top, right + 1, bottom + diameter + 1, diameter, diameter);
            try { LocalPInvoke.CombineRgn(region, region, pill, 2); }
            finally { LocalPInvoke.DeleteObject(pill); }
        }

        private static void AnimateHoverRegion(IntPtr region, HoverTransition transition, Types.SegmentSettings settings)
        {
            if (settings?.ShowOnHover != true || (transition.Opening ? settings.HoverOpenEffect : settings.HoverCloseEffect) == Types.HoverAnimationEffect.None
                || !transition.Initialized || transition.Progress >= 1) return;
            if (LocalPInvoke.GetRgnBox(region, out var bounds) == 0) return;
            var clip = transition.Clip(bounds, settings);
            var clippingRegion = LocalPInvoke.CreateRectRgn(clip.Left, clip.Top, clip.Right, clip.Bottom);
            try { LocalPInvoke.CombineRgn(region, region, clippingRegion, 1); }
            finally { LocalPInvoke.DeleteObject(clippingRegion); }
        }

        private static readonly Dictionary<uint, bool> previewProcesses = new();
        private static readonly HashSet<IntPtr> taskbarPreviewHosts = new();

        private static void RememberTaskbarPreview(IntPtr window, string className, Types.Taskbar taskbar)
        {
            // Thumbnail hosts start without focus while the app button is hovered.
            // Closing a thumbnail can focus the same host; that does not turn it
            // into Alt+Tab. Task View takes focus when it opens.
            if (className == "XamlExplorerHostIslandWindow" && taskbar.AppListHovered
                && LocalPInvoke.GetForegroundWindow() != window)
            {
                if (taskbarPreviewHosts.Count > 32) taskbarPreviewHosts.Clear();
                taskbarPreviewHosts.Add(window);
            }
        }

        internal static bool IsTaskbarPopupClass(string name)
            => name == "TaskListThumbnailWnd" || name == "Xaml_WindowedPopupClass"
                || name == "XamlExplorerHostIslandWindow";

        internal static bool PreviewRequiresAdjacentBounds(string name)
            => name != "TaskListThumbnailWnd" && name != "XamlExplorerHostIslandWindow";

        internal static bool IsThumbnailPreviewCandidate(string className, bool knownThumbnailHost, bool movingWindow)
            => !movingWindow && IsTaskbarPopupClass(className)
                && (className != "XamlExplorerHostIslandWindow" || knownThumbnailHost);

        internal static bool IsPopupAdjacentToApps(LocalPInvoke.RECT popup, Types.Taskbar taskbar)
        {
            var scale = taskbar.ScaleFactor > 0 ? taskbar.ScaleFactor : 1;
            var gap = (int)(64 * scale);
            if (popup.Right <= popup.Left || popup.Bottom <= popup.Top
                || popup.Bottom - popup.Top > 600 * scale) return false;
            var besideEdge = System.Math.Abs(popup.Bottom - taskbar.TaskbarRect.Top) <= gap
                || System.Math.Abs(popup.Top - taskbar.TaskbarRect.Bottom) <= gap;
            return besideEdge && popup.Left < taskbar.AppListRect.Right
                && popup.Right > taskbar.AppListRect.Left;
        }

        internal static bool IsWindowPreviewOpenOnMonitor(Types.Taskbar taskbar)
        {
            var gui = new LocalPInvoke.GUITHREADINFO { Size = (uint)Marshal.SizeOf<LocalPInvoke.GUITHREADINFO>() };
            if (LocalPInvoke.GetGUIThreadInfo(0, ref gui) && GetMovingWindow(gui) != IntPtr.Zero)
                return false;
            // Modern preview surfaces may be children of an Explorer host,
            // rather than independently enumerable top-level windows.
            if (LocalPInvoke.GetCursorPos(out var cursor))
            {
                var pointed = LocalPInvoke.WindowFromPoint(cursor);
                var name = new System.Text.StringBuilder(256);
                for (var depth = 0; pointed != IntPtr.Zero && depth < 8; depth++, pointed = LocalPInvoke.GetParent(pointed))
                {
                    LocalPInvoke.GetClassName(pointed, name, name.Capacity);
                    if (!IsTaskbarPopupClass(name.ToString()) || !LocalPInvoke.IsWindowVisible(pointed)
                        || !LocalPInvoke.GetWindowRect(pointed, out var bounds)
                        || (PreviewRequiresAdjacentBounds(name.ToString()) && !IsPopupAdjacentToApps(bounds, taskbar))
                        || LocalPInvoke.MonitorFromWindow(pointed, 2) != LocalPInvoke.MonitorFromWindow(taskbar.TaskbarHwnd, 2)) continue;
                    LocalPInvoke.GetWindowThreadProcessId(pointed, out var owner);
                    if (IsExplorerPreviewProcess(owner))
                    {
                        RememberTaskbarPreview(pointed, name.ToString(), taskbar);
                        if (IsThumbnailPreviewCandidate(name.ToString(), taskbarPreviewHosts.Contains(pointed), false)) return true;
                    }
                }
            }
            // Windows 11 uses XAML PopupHost windows for taskbar flyouts; these
            // do not take foreground focus. Keep checking legacy thumbnails too.
            foreach (var className in new[] { "TaskListThumbnailWnd", "Xaml_WindowedPopupClass", "XamlExplorerHostIslandWindow" })
            {
                var window = IntPtr.Zero;
                while ((window = LocalPInvoke.FindWindowEx(IntPtr.Zero, window, className, null)) != IntPtr.Zero)
                {
                    if (!LocalPInvoke.IsWindowVisible(window)) continue;
                    if (LocalPInvoke.MonitorFromWindow(window, 2) != LocalPInvoke.MonitorFromWindow(taskbar.TaskbarHwnd, 2)) continue;
                    if (LocalPInvoke.DwmGetWindowAttribute(window, LocalPInvoke.DWMWINDOWATTRIBUTE.Cloaked,
                        out bool cloaked, 4) != 0 || cloaked) continue;
                    if (className == "TaskListThumbnailWnd") return true;
                    // The thumbnail host's HWND covers the whole monitor. Its
                    // visible/uncloaked lifetime, not its bounds or the cursor,
                    // is the open-preview signal (including the gap below it).
                    if (PreviewRequiresAdjacentBounds(className)
                        && (!LocalPInvoke.GetWindowRect(window, out var rect) || !IsPopupAdjacentToApps(rect, taskbar))) continue;
                    LocalPInvoke.GetWindowThreadProcessId(window, out var pid);
                    if (IsExplorerPreviewProcess(pid))
                    {
                        RememberTaskbarPreview(window, className, taskbar);
                        if (IsThumbnailPreviewCandidate(className, taskbarPreviewHosts.Contains(window), false)) return true;
                    }
                }
            }
            return false;
        }

        private static bool IsExplorerPreviewProcess(uint pid)
        {
            if (!previewProcesses.TryGetValue(pid, out var explorer))
            {
                explorer = false;
                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    explorer = string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
                if (previewProcesses.Count > 32) previewProcesses.Clear();
                previewProcesses[pid] = explorer;
            }
            return explorer;
        }

        public static bool TaskbarShouldBeFilled(IntPtr taskbarHwnd, Types.Settings settings, bool? hasMaximizedWindow = null)
        {
            if (settings.IsWindowsDefault) return true;
            var movingWindow = IntPtr.Zero;
            var gui = new LocalPInvoke.GUITHREADINFO { Size = (uint)Marshal.SizeOf<LocalPInvoke.GUITHREADINFO>() };
            if (LocalPInvoke.GetGUIThreadInfo(0, ref gui))
            {
                movingWindow = GetMovingWindow(gui);
                if (movingWindow != IntPtr.Zero)
                {
                    if (WorkAreaExpansion.RestoreOnMaximize(settings) && IsAtMaximizeSnapTarget(taskbarHwnd)) return true;
                    // Ignore the dragged window's stale maximized state, but still
                    // check other maximized windows on this monitor.
                }
            }
            var windows = Interaction.GetTopLevelWindows();
            if (settings.FillOnTaskSwitch && IsTaskSwitchOpen(windows)) return true;
            if (!WorkAreaExpansion.RestoreOnMaximize(settings)) return false;
            return hasMaximizedWindow ?? HasMaximizedWindowOnMonitor(taskbarHwnd, windows, movingWindow);
        }

        internal static bool IsTaskSwitchOpen(IEnumerable<IntPtr> windows)
        {
            var foreground = LocalPInvoke.GetForegroundWindow();
            var foregroundClass = new StringBuilder(256);
            LocalPInvoke.GetClassName(foreground, foregroundClass, foregroundClass.Capacity);
            if (IsTaskSwitcherWindow(foregroundClass.ToString(), isForeground: true,
                isThumbnailPreview: taskbarPreviewHosts.Contains(foreground))) return true;

            // Only explicit switcher windows qualify. Snap previews and brief
            // focus changes after dragging must not restore the full taskbar.
            foreach (var window in windows)
            {
                if (!LocalPInvoke.IsWindowVisible(window)) continue;
                var className = new StringBuilder(256);
                LocalPInvoke.GetClassName(window, className, className.Capacity);
                if (!IsTaskSwitcherWindow(className.ToString(), isForeground: false)) continue;
                if (LocalPInvoke.DwmGetWindowAttribute(window, LocalPInvoke.DWMWINDOWATTRIBUTE.Cloaked,
                    out bool cloaked, 4) == 0 && !cloaked) return true;
            }
            return false;
        }

        internal static bool HasMaximizedWindowOnMonitor(IntPtr taskbarHwnd, IEnumerable<IntPtr> windows = null)
        {
            var gui = new LocalPInvoke.GUITHREADINFO { Size = (uint)Marshal.SizeOf<LocalPInvoke.GUITHREADINFO>() };
            var moving = LocalPInvoke.GetGUIThreadInfo(0, ref gui) ? GetMovingWindow(gui) : IntPtr.Zero;
            return HasMaximizedWindowOnMonitor(taskbarHwnd, windows ?? Interaction.GetTopLevelWindows(), moving);
        }

        private static bool HasMaximizedWindowOnMonitor(IntPtr taskbarHwnd, IEnumerable<IntPtr> windows, IntPtr movingWindow)
        {
            foreach (var window in windows)
            {
                if (window == movingWindow || !LocalPInvoke.IsWindowVisible(window)
                    || LocalPInvoke.MonitorFromWindow(taskbarHwnd, 2) != LocalPInvoke.MonitorFromWindow(window, 2)) continue;
                LocalPInvoke.DwmGetWindowAttribute(window, LocalPInvoke.DWMWINDOWATTRIBUTE.Cloaked, out bool cloaked, 4);
                if (cloaked) continue;
                var placement = LocalPInvoke.WINDOWPLACEMENT.Default;
                if (LocalPInvoke.GetWindowPlacement(window, ref placement)
                    && MaximizedWindowTaskbarAccess.Eligible(LocalPInvoke.GetWindowStyleBits(window, -16), placement.ShowCmd))
                {
                    var className = new StringBuilder(256);
                    LocalPInvoke.GetClassName(window, className, className.Capacity);
                    if (!IsShellOverlayClass(className.ToString())) return true;
                }
            }
            return false;
        }

        private static bool IsAtMaximizeSnapTarget(IntPtr taskbarHwnd)
        {
            var monitor = new MonitorStuff.MONITORINFO { cbSize = (uint)Marshal.SizeOf<MonitorStuff.MONITORINFO>() };
            if (!MonitorStuff.GetMonitorInfo(LocalPInvoke.MonitorFromWindow(taskbarHwnd, 2), ref monitor)
                || !LocalPInvoke.GetCursorPos(out var cursor)) return false;
            double scale = LocalPInvoke.GetDpiForWindow(taskbarHwnd) / 96.0;
            // The shell preview is composed independently and its HWND bounds need
            // not match the preview rectangle. Use the native move loop and its snap
            // target instead; this also stops qualifying immediately on release.
            return IsMaximizeSnapTarget(monitor.rcMonitor, cursor, scale);
        }

        internal static bool IsMaximizeSnapTarget(LocalPInvoke.RECT monitor, LocalPInvoke.POINT cursor, double scale)
        {
            int edge = (int)Math.Ceiling(8 * scale);
            int corner = (int)Math.Ceiling(32 * scale);
            return cursor.y >= monitor.Top && cursor.y <= monitor.Top + edge
                && cursor.x > monitor.Left + corner && cursor.x < monitor.Right - corner;
        }

        internal static IntPtr GetMovingWindow(LocalPInvoke.GUITHREADINFO info) =>
            (info.Flags & LocalPInvoke.GUI_INMOVESIZE) != 0
                ? (info.MoveSize != IntPtr.Zero ? info.MoveSize : info.Active)
                : IntPtr.Zero;

        internal static bool IsShellOverlayClass(string className) =>
            className is "XamlExplorerHostIslandWindow" or "MultitaskingViewFrame" or "TaskSwitcherWnd";

        internal static bool IsTaskSwitcherWindow(string className, bool isForeground, bool isThumbnailPreview = false) =>
            className is "MultitaskingViewFrame" or "TaskSwitcherWnd"
            // Snap previews use this class too and can outlive the window move loop.
            // Actual Alt+Tab and Task View take foreground focus; previews do not.
            || (isForeground && !isThumbnailPreview && className == "XamlExplorerHostIslandWindow");

        internal static void EnsureLayered(Types.Taskbar taskbar)
        {
            int style = LocalPInvoke.GetWindowLong(taskbar.TaskbarHwnd, LocalPInvoke.GWL_EXSTYLE).ToInt32();
            if ((style & LocalPInvoke.WS_EX_LAYERED) == 0)
            {
                LocalPInvoke.SetWindowLong(taskbar.TaskbarHwnd, LocalPInvoke.GWL_EXSTYLE, style | LocalPInvoke.WS_EX_LAYERED);
                LocalPInvoke.SetLayeredWindowAttributes(taskbar.TaskbarHwnd, 0, 255, LocalPInvoke.LWA_ALPHA);
            }
        }

        /// <summary>
        /// Sets the appbar properties of the taskbar.
        /// </summary>
        public static void SetTaskbarState(LocalPInvoke.AppBarStates option, IntPtr hwnd)
        {
            LocalPInvoke.APPBARDATA msgData = new LocalPInvoke.APPBARDATA();
            msgData.cbSize = (uint)Marshal.SizeOf(msgData);
            msgData.hWnd = hwnd;
            msgData.lParam = (int)option;
            LocalPInvoke.SHAppBarMessage(LocalPInvoke.ABM.SetState, ref msgData);
        }


    }
}
