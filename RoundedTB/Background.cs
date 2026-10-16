using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace RoundedTB
{
    public class Background
    {
        // Just have a reference point for the Dispatcher
        public MainWindow mw;
        bool redrawOverride = false;
        private readonly ActivationStripSurfaces stripSurfaces = new();
        private readonly MaximizedWindowTaskbarAccess maximizedWindowAccess = new();
        private readonly AppOpenReveal appOpenReveal = new();
        private readonly FullscreenWindowBounds fullscreenWindowBounds = new();

        public Background(MainWindow window)
        {
            mw = window;
        }


        internal void CloseStripSurfaces() => stripSurfaces.Close();

        // Main method for the BackgroundWorker - runs indefinitely
        public void DoWork(object sender, DoWorkEventArgs e)
        {
            StartupDiagnostics.WorkerStarted();
            mw.interaction.AddLog("in bw");
            BackgroundWorker worker = sender as BackgroundWorker;
            try
            {
                while (true)
                {
                    try
                    {
                        if (worker.CancellationPending == true)
                        {
                            mw.interaction.AddLog("cancelling");
                            e.Cancel = true;
                            break;
                        }

                        // Primary loop for the running process
                        else
                        {
                            var frameStart = Stopwatch.GetTimestamp();
                            // Check if the taskbar is centred, and if it is, directly update the settings; using an interim bool to avoid delaying because I'm lazy
                            bool isCentred = Taskbar.CheckIfCentred();
                            mw.activeSettings.IsCentred = isCentred;

                            // Work with static values to avoid some null reference exceptions
                            List<Types.Taskbar> taskbars = mw.taskbarDetails;
                            Types.Settings settings = mw.activeSettings;
                            maximizedWindowAccess.Update(WorkAreaExpansion.Enabled(settings));
                            mw.MaintainExpandedWorkAreas();
                            // If the number of taskbars has changed, regenerate taskbar information
                            if (taskbars.Count == 0 || Taskbar.TaskbarCountOrHandleChanged(taskbars.Count, taskbars[0].TaskbarHwnd))
                            {
                                // Forcefully reset taskbars if the taskbar count or main taskbar handle has changed
                                taskbars = Taskbar.GenerateTaskbarInfo(mw.activeSettings.IsWindows11 && mw.activeSettings.IsDynamic);
                                Debug.WriteLine("Regenerating taskbar info");
                            }

                            var visibilityWindows = Interaction.GetTopLevelWindows();
                            foreach (var taskbar in taskbars)
                            {
                                bool maximized = Taskbar.HasMaximizedWindowOnMonitor(taskbar.TaskbarHwnd, visibilityWindows);
                                if (taskbar.HasMaximizedWindow != maximized) taskbar.Ignored = true;
                                taskbar.HasMaximizedWindow = maximized;
                            }
                            appOpenReveal.Update(taskbars, settings);
                            mw.appAttentionReveal.Update(taskbars, settings);
                            for (int current = 0; current < taskbars.Count; current++)
                            {
                                if (taskbars[current].TaskbarHwnd == IntPtr.Zero || (taskbars[current].AppListHwnd == IntPtr.Zero && !settings.IsWindows11))
                                {
                                    taskbars = Taskbar.GenerateTaskbarInfo(mw.activeSettings.IsWindows11 && mw.activeSettings.IsDynamic);
                                    Debug.WriteLine("Regenerating taskbar info due to a missing handle");
                                    break;
                                }
                                var effectiveSettings = WindowModeVisibility.Resolve(settings, taskbars[current].HasMaximizedWindow);
                                bool startMenuOpen = Taskbar.IsStartMenuOpenOnMonitor(taskbars[current].TaskbarHwnd);
                                fullscreenWindowBounds.Update(taskbars[current].TaskbarHwnd, visibilityWindows);
                                bool revealOverFullscreen = startMenuOpen
                                    || (effectiveSettings.FillOnTaskSwitch && Taskbar.IsTaskSwitchOpen(visibilityWindows));
                                if (Taskbar.HideFullscreenEdge(taskbars[current], revealOverFullscreen)) continue;
                                // Get the latest quick details of this taskbar

                                // If the taskbar's monitor has a maximised window, reset it so it's "filled"
                                bool restoreRequested = Taskbar.TaskbarShouldBeFilled(taskbars[current].TaskbarHwnd, effectiveSettings, taskbars[current].HasMaximizedWindow);
                                bool revealSegments = restoreRequested && WorkAreaExpansion.Enabled(effectiveSettings) && !effectiveSettings.IsWindowsDefault;
                                if (taskbars[current].RevealAllSegments != revealSegments)
                                {
                                    taskbars[current].RevealAllSegments = revealSegments;
                                    taskbars[current].Ignored = true;
                                }
                                if (restoreRequested && !revealSegments)
                                {
                                    if (!taskbars[current].AppearanceRestored)
                                    {
                                        Taskbar.ResetTaskbar(taskbars[current], effectiveSettings, removeLayeredStyle: false);
                                        taskbars[current].AppearanceRestored = true;
                                        taskbars[current].Ignored = true;
                                        taskbars[current].TaskbarHidden = false;
                                    }
                                    continue;
                                }

                                if (taskbars[current].AppearanceRestored)
                                {
                                    // Reuse the last measured layout immediately; UI Automation can
                                    // take longer while Explorer is finishing its window animation.
                                    taskbars[current].AppearanceRestored = false;
                                    Taskbar.EnsureLayered(taskbars[current]);
                                    if (effectiveSettings.IsDynamic) Taskbar.UpdateDynamicTaskbar(taskbars[current], effectiveSettings);
                                    else Taskbar.UpdateSimpleTaskbar(taskbars[current], effectiveSettings);
                                    taskbars[current].Ignored = true;
                                }

                                Taskbar.EnsureVisible(taskbars[current]);
                                Types.Taskbar newTaskbar = Taskbar.GetQuickTaskbarRects(taskbars[current].TaskbarHwnd, taskbars[current].TrayHwnd, taskbars[current].AppListHwnd, effectiveSettings.IsWindows11 && effectiveSettings.IsDynamic);
                                if (newTaskbar.TrayHwnd != taskbars[current].TrayHwnd)
                                {
                                    taskbars[current].TrayHwnd = newTaskbar.TrayHwnd;
                                    taskbars[current].TrayRect = newTaskbar.TrayRect;
                                    taskbars[current].Ignored = true;
                                }

                                newTaskbar.ScaleFactor = Math.Max(1, LocalPInvoke.GetDpiForWindow(newTaskbar.TaskbarHwnd) / 96.0);
                                if (newTaskbar.ScaleFactor != taskbars[current].ScaleFactor)
                                {
                                    taskbars[current].ScaleFactor = newTaskbar.ScaleFactor;
                                    taskbars[current].Ignored = true;
                                }

                                if (taskbars[current].Ignored) Taskbar.EnsureLayered(taskbars[current]);

                                // Hover changes the region even when the native icon layout is unchanged.
                                if ((SegmentHover.Enabled(effectiveSettings) || startMenuOpen || taskbars[current].StartMenuOpen) && LocalPInvoke.GetCursorPos(out var cursor))
                                {
                                    if (SegmentHover.Update(taskbars[current], newTaskbar, cursor,
                                        startMenuOpen,
                                        Taskbar.IsWindowPreviewOpenOnMonitor(taskbars[current]) || AppOpenReveal.Active(taskbars[current], effectiveSettings), effectiveSettings,
                                        trayPopupOpen: Taskbar.IsTrayPopupOpenOnMonitor(taskbars[current].TaskbarHwnd)))
                                    {
                                        taskbars[current].TrayRect = newTaskbar.TrayRect;
                                        taskbars[current].WidgetsRect = newTaskbar.WidgetsRect;
                                        if (effectiveSettings.IsDynamic) Taskbar.UpdateDynamicTaskbar(taskbars[current], effectiveSettings);
                                        else Taskbar.UpdateSimpleTaskbar(taskbars[current], effectiveSettings);
                                    }
                                }

                                // If the taskbar's overall rect has changed, update it. If it's simple, just update. If it's dynamic, check it's a valid change, then update it.
                                if (Taskbar.TaskbarRefreshRequired(taskbars[current], newTaskbar, effectiveSettings.IsDynamic) || taskbars[current].Ignored || redrawOverride)
                                {
                                    Debug.WriteLine($"Refresh required on taskbar {current}");
                                    taskbars[current].Ignored = false;
                                    int isFullTest = newTaskbar.TrayRect.Left - newTaskbar.AppListRect.Right;
                                    if (!effectiveSettings.IsDynamic || (!newTaskbar.HasMeasuredAppListBounds && isFullTest <= taskbars[current].ScaleFactor * 25 && isFullTest > 0 && newTaskbar.TrayRect.Left != 0))
                                    {
                                        // Add the rect changes to the temporary list of taskbars
                                        taskbars[current].TaskbarRect = newTaskbar.TaskbarRect;
                                        taskbars[current].AppListRect = newTaskbar.AppListRect;
                                        taskbars[current].HasMeasuredAppListBounds = newTaskbar.HasMeasuredAppListBounds;
                                        taskbars[current].WidgetsRect = newTaskbar.WidgetsRect;
                                        taskbars[current].TrayRect = newTaskbar.TrayRect;
                                        Taskbar.UpdateSimpleTaskbar(taskbars[current], effectiveSettings);
                                    }
                                    else
                                    {
                                        if (Taskbar.CheckDynamicUpdateIsValid(taskbars[current], newTaskbar))
                                        {
                                            // Add the rect changes to the temporary list of taskbars
                                            taskbars[current].TaskbarRect = newTaskbar.TaskbarRect;
                                            taskbars[current].AppListRect = newTaskbar.AppListRect;
                                            taskbars[current].HasMeasuredAppListBounds = newTaskbar.HasMeasuredAppListBounds;
                                            taskbars[current].WidgetsRect = newTaskbar.WidgetsRect;
                                            taskbars[current].TrayRect = newTaskbar.TrayRect;
                                            Taskbar.UpdateDynamicTaskbar(taskbars[current], effectiveSettings);
                                        }
                                    }
                                }
                            }
                            mw.taskbarDetails = taskbars;
                            stripSurfaces.Update(ActivationStripSurfaces.Describe(taskbars, settings));


                            // Include measurement time in the frame budget instead of adding
                            // a fixed delay after it. Keep disk writes out of moving layouts.
                            int frameBudget = settings.IsWindows11 && settings.IsDynamic ? 8 : 25;
                            int remaining = frameBudget - (int)Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds;
                            System.Threading.Thread.Sleep(Math.Max(1, remaining));
                        }
                    }
                    catch (TypeInitializationException ex)
                    {
                        mw.interaction.AddLog(ex.Message);
                        mw.interaction.AddLog(ex.InnerException.Message);
                        throw;
                    }
                }
            }
            finally
            {
                appOpenReveal.Reset();
                maximizedWindowAccess.Close();
                stripSurfaces.Close();
                TaskbarIconBounds.Clear();
            }
        }
    }
}
