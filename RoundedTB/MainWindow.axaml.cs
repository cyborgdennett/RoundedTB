using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using System.Diagnostics;
using Microsoft.Win32;
using System.Text;

namespace RoundedTB
{
    /// <summary>
    /// Interaction logic for MainWindow.axaml
    /// 
    /// Many thanks to
    ///  - FloatingMilkshake
    ///  - cardin
    ///  - cleverActon0126
    ///  for your gracious donations! 💖
    ///  
    /// </summary>
    public partial class MainWindow : Window
    {
        public bool isWindows11;
        public List<Types.Taskbar> taskbarDetails = new List<Types.Taskbar>();
        public bool shouldReallyDieNoReally = false;
        public string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rtb.json");
        public string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rtb.log");
        public Types.Settings activeSettings = new Types.Settings();
        public BackgroundWorker taskbarThread = new BackgroundWorker();
        public IntPtr hwndDesktopButton = IntPtr.Zero;
        public int lastDynDistance = 0;
        public int numberToForceRefresh = 0;
        public bool isCentred = false;
        public Background background;
        internal readonly AppAttentionReveal appAttentionReveal = new();
        internal uint ShellHookMessage { get; private set; }
        private bool shellHookRegistered;
        public Interaction interaction;
        private bool initialized;
        private bool loadingEditor;
        private bool initializationStarted;
        private bool applying;
        private readonly DispatcherTimer saveEditorTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
        public IntPtr NativeHandle { get; private set; }
        private volatile bool exiting;
        private TrayIcon trayIcon;
        public NativeMenuItem ShowMenuItem = new NativeMenuItem("Show RoundedTB");
        public int selectedSegment = 0; // 0 = Simple, 1 = AppList, 2 = Tray, 3 = Widgets
        public int version = -1;
        /// <summary>
        /// Versions:
        /// -1: Canary
        ///  0: R3.0
        ///  1: P3.1B
        ///  2: R3.1
        ///  3: R4
        /// </summary>

        public MainWindow()
        {
            StartupDiagnostics.Mark("window-construction-start");
            InitializeComponent();
            AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                if (e.Source is Visual target &&
                    !target.GetVisualAncestors().Prepend(target).OfType<NumericUpDown>().Any() &&
                    FocusManager?.GetFocusedElement() is Visual focused &&
                    focused.GetVisualAncestors().Prepend(focused).OfType<NumericUpDown>().Any())
                    FocusManager.Focus(null);
            }, RoutingStrategies.Tunnel);
            SizeChanged += (_, _) => UpdateSettingsNavigation(ClientSize.Width);
#if DEBUG
            Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://RoundedTB/RoundedTBCanary.ico")));
#endif
            StartupDiagnostics.Mark("window-resources-loaded");
            if (OperatingSystem.IsWindows())
            {
                taskbarButtonCreatedMessage = (uint)LocalPInvoke.RegisterWindowMessage("TaskbarButtonCreated");
                Win32Properties.AddWndProcHookCallback(this, TaskbarButtonHook);
            }
            Activated += (_, _) => StopAttentionTest();
            foreach (var input in new[] { cornerRadiusInput, mTopInput, mBottomInput, mLeftInput, mRightInput, hoverDelayInput, hoverOpenDurationInput, hoverCloseDurationInput, hoverPillWidthInput, hoverPillThicknessInput, appOpenDurationInput, appAttentionDurationInput })
                input.AddHandler(InputElement.KeyDownEvent, NumericInput_KeyDown, RoutingStrategies.Tunnel);
            saveEditorTimer.Tick += (_, _) =>
            {
                saveEditorTimer.Stop();
                try { if (!exiting) interaction?.WriteJSON(); }
                catch (Exception error) { interaction?.AddLog(error.ToString()); }
            };
            Closed += (_, _) => saveEditorTimer.Stop();
            Opened += async (_, _) =>
            {
                StartupDiagnostics.Mark("window-opened");
                var screen = Screens.ScreenFromWindow(this);
                if (screen != null)
                {
                    Width = Math.Min(Width, Math.Max(MinWidth, screen.WorkingArea.Width / screen.Scaling - 32));
                    Height = Math.Min(Height, Math.Max(MinHeight, screen.WorkingArea.Height / screen.Scaling - 32));
                }
                await StartAsync();
            };
            dynamicCheckBox.IsCheckedChanged += (_, e) =>
            {
                if (!initialized) return;
                if (dynamicCheckBox.IsChecked == true) dynamicCheckBox_Checked(null, e);
                else dynamicCheckBox_Unchecked(null, e);
            };
            fillMaximisedCheckBox.IsCheckedChanged += (_, e) =>
            {
                if (!initialized) return;
                if (fillMaximisedCheckBox.IsChecked == true) fillMaximisedCheckBox_Checked(null, e);
                else fillMaximisedCheckBox_Unchecked(null, e);
            };
            showSegmentsOnHoverCheckBox.IsCheckedChanged += (_, e) =>
            {
                if (!initialized || loadingEditor) return;
                if (showSegmentsOnHoverCheckBox.IsChecked == true) showSegmentsOnHoverCheckBox_Checked(null, e);
                else showSegmentsOnHoverCheckBox_Unchecked(null, e);
            };
            revealOnAppOpenCheckBox.IsCheckedChanged += (_, _) => ApplyLiveSettings();
            revealOnAppAttentionCheckBox.IsCheckedChanged += (_, _) => ApplyLiveSettings();
            keepAppAttentionOpenCheckBox.IsCheckedChanged += (_, _) => ApplyLiveSettings();
            expandWorkAreaCheckBox.IsCheckedChanged += (_, _) => ApplyLiveSettings();
            fillMaximisedCheckBox.IsCheckedChanged += (_, _) => ApplyLiveSettings();
            fillAltTabCheckBox.IsCheckedChanged += (_, _) => ApplyLiveSettings();
            appOpenDurationInput.ValueChanged += (_, _) => ApplyLiveSettings();
            appAttentionDurationInput.ValueChanged += (_, _) => ApplyLiveSettings();

        }

        internal async Task StartAsync()
        {
            if (initializationStarted) return;
            initializationStarted = true;
            try { await InitializeAsync(); }
            catch (Exception error)
            {
                interaction?.AddLog(error.ToString());
                var info = new Infobox();
                info.titleBlock.Text = "Unable to start RoundedTB";
                info.bodyBlock.Text = error.Message;
                Show();
                await info.ShowDialog(this);
                Exit();
            }
        }

        private async Task InitializeAsync()
        {
            StartupDiagnostics.Mark("initialization-start");
            background = new Background(this);
            interaction = new Interaction(this);
            // Check OS build, as behaviours rather-annoyingly differ between Windows 11 and Windows 10
            using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var buildNumber = registryKey.GetValue("CurrentBuild").ToString();
            if (Convert.ToInt32(buildNumber) >= 21996)
            {
                isWindows11 = true;
            }
            else
            {
                isWindows11 = false;
                activeSettings.IsWindows11 = false;
                dynamicCheckBox.Content = "Separate apps and system tray";
                fillAltTabCheckBox.Content = "[Unavailable]";
            }

            if (IsRunningAsUWP())
            {
                await StartupInit(true);
                configPath = Path.Combine(Windows.Storage.ApplicationData.Current.RoamingFolder.Path, "rtb.json");
                logPath = Path.Combine(Windows.Storage.ApplicationData.Current.RoamingFolder.Path, "rtb.log");
            }

            StartupDiagnostics.Mark("platform-detected");
            // Reading Task Scheduler may block while its service initializes at sign-in.
            // The taskbar does not depend on this checkbox, so query it independently.
            if (!IsRunningAsUWP()) _ = RefreshStartupRegistrationAsync();
            taskbarThread.WorkerSupportsCancellation = true;
            taskbarThread.WorkerReportsProgress = true;
            taskbarThread.DoWork += background.DoWork;
            taskbarThread.RunWorkerCompleted += async (_, result) =>
            {
                if (result.Error == null || exiting) return;
                interaction.AddLog(result.Error.ToString());
                Show();
                var info = new Infobox();
                info.titleBlock.Text = "The taskbar worker stopped";
                info.bodyBlock.Text = result.Error.Message + "\n\nDetails were saved to " + logPath;
                await info.ShowDialog(this);
            };

            // Load settings into memory/UI
            StartupDiagnostics.Mark("settings-read-start");
            interaction.FileSystem();
            if (!IsRunningAsUWP())
            {
                interaction.AddLog($"RoundedTB started!");
            }
            else
            {
                interaction.AddLog($"RoundedTB started in UWP mode!");
            }
            activeSettings = interaction.ReadJSON();
            StartupDiagnostics.Mark("settings-read-complete");

            // Default settings
            if (activeSettings == null)
            {
                
                if (isWindows11) // Default settings for Windows 11
                {
                    activeSettings = new Types.Settings()
                    {
                        SimpleTaskbarLayout = new Types.SegmentSettings{ CornerRadius = 7, MarginLeft = 3, MarginTop = 3, MarginRight = 3, MarginBottom = 3 },
                        DynamicAppListLayout = new Types.SegmentSettings { CornerRadius = 7, MarginLeft = 3, MarginTop = 3, MarginRight = 3, MarginBottom = 3 },
                        DynamicTrayLayout = new Types.SegmentSettings { CornerRadius = 7, MarginLeft = 3, MarginTop = 3, MarginRight = 3, MarginBottom = 3 },
                        DynamicWidgetsLayout = new Types.SegmentSettings { CornerRadius = 7, MarginLeft = 3, MarginTop = 3, MarginRight = 3, MarginBottom = 3 },
                        IsDynamic = false,
                        IsCentred = false,
                        IsWindows11 = true,
                        ShowTray = false,
                        ShowWidgets = false,
                        IsNotFirstLaunch = false,
                        FillOnMaximise = true,
                        FillOnTaskSwitch = true,
                        ShowSegmentsOnHover = false,
                        AutoHide = 0
                    };
                }
                else // Default settings for Windows 10
                {
                    activeSettings = new Types.Settings()
                    {
                        SimpleTaskbarLayout = new Types.SegmentSettings { CornerRadius = 16, MarginLeft = 2, MarginTop = 2, MarginRight = 2, MarginBottom = 2 },
                        DynamicAppListLayout = new Types.SegmentSettings { CornerRadius = 16, MarginLeft = 2, MarginTop = 2, MarginRight = 2, MarginBottom = 2 },
                        DynamicTrayLayout = new Types.SegmentSettings { CornerRadius = 16, MarginLeft = 2, MarginTop = 2, MarginRight = 2, MarginBottom = 2 },
                        DynamicWidgetsLayout = new Types.SegmentSettings { CornerRadius = 16, MarginLeft = 2, MarginTop = 2, MarginRight = 2, MarginBottom = 2 },
                        IsDynamic = false,
                        IsCentred = false,
                        IsWindows11 = false,
                        ShowTray = false,
                        ShowWidgets = false,
                        IsNotFirstLaunch = false,
                        FillOnMaximise = true,
                        FillOnTaskSwitch = false,
                        ShowSegmentsOnHover = false,
                        AutoHide = 0
                    };
                }
            }

            activeSettings.IsWindows11 = isWindows11;

            if (version != activeSettings.Version && version != -1)
            {
                activeSettings.IsNotFirstLaunch = false;
            }
            activeSettings.Version = version;


            interaction.AddLog($"Settings loaded:");
            interaction.AddLog(
                $"SimpleTaskbarLayout: {activeSettings.SimpleTaskbarLayout}\n" +
                $"DynamicAppListLayout: {activeSettings.DynamicAppListLayout}\n" +
                $"DynamicTrayLayout: {activeSettings.DynamicTrayLayout}\n" +
                $"DynamicWidgetsLayout: {activeSettings.DynamicWidgetsLayout}\n" +
                $"IsDynamic: {activeSettings.IsDynamic}\n" +
                $"IsCentred: {activeSettings.IsCentred}\n" +
                $"ShowTray: {activeSettings.ShowTray}\n" +
                $"ShowWidgets: {activeSettings.ShowWidgets}\n" +
                $"IsNotFirstLaunch: {activeSettings.IsNotFirstLaunch}\n" +
                $"FillOnMaximise: {activeSettings.FillOnMaximise}\n" +
                $"FillOnTaskSwitch: {activeSettings.FillOnTaskSwitch}\n" +
                $"ShowTrayOnHover: {activeSettings.ShowSegmentsOnHover}\n"
                );

            // Get whether or not taskbar is centred
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced"))
                {
                    if (key != null)
                    {
                        int val = (int)key.GetValue("TaskbarAl");
                        if (val == 1)
                        {
                            isCentred = true;
                        }
                        else
                        {
                            isCentred = false;
                        }
                        interaction.AddLog($"Taskbar centred? {isCentred}");
                    }
                }
            }
            catch (Exception aaaa)
            {
                interaction.AddLog(aaaa.Message);
            }
            if (!isWindows11)
            {
                activeSettings.IsCentred = false;
            }

            // Rounded areas are the enabled mode; Windows defaults use the master control.
            if (isWindows11) activeSettings.IsDynamic = true;
            InitializeUi(activeSettings, isWindows11);
            StartupDiagnostics.Mark("settings-loaded");
            // A logon task can start before Explorer creates its taskbar.
            if (Environment.GetCommandLineArgs().Contains("--startup"))
            {
                while (!exiting && LocalPInvoke.FindWindow("Shell_TrayWnd", null) == IntPtr.Zero)
                    await Task.Delay(100);
                if (exiting) return;
            }
            StartupDiagnostics.Mark("explorer-taskbar-available");
            taskbarDetails = await Task.Run(() => Taskbar.GenerateTaskbarInfo(activeSettings.IsWindows11 && activeSettings.IsDynamic, !activeSettings.IsWindowsDefault));
            Win32Properties.AddWndProcHookCallback(this, interaction.HwndHook);
            NativeHandle = TryGetPlatformHandle().Handle;
            sessionNotificationsRegistered = LocalPInvoke.WTSRegisterSessionNotification(NativeHandle, 0);
            if (!sessionNotificationsRegistered) interaction.AddLog("Session notification registration failed: " + Marshal.GetLastWin32Error());
            LocalPInvoke.RegisterHotKey(NativeHandle, 9000, 0x8, 0x71);
            ShellHookMessage = (uint)LocalPInvoke.RegisterWindowMessage("SHELLHOOK");
            shellHookRegistered = ShellHookMessage != 0 && LocalPInvoke.RegisterShellHookWindow(NativeHandle);
            if (!shellHookRegistered)
            {
                appAttentionHint.Text = "Windows did not allow flashing-app detection. Other auto-hide features remain available.";
                interaction.AddLog("RegisterShellHookWindow failed: " + Marshal.GetLastWin32Error());
            }
            CreateTrayIcon();
            StartupDiagnostics.Mark("tray-created");
            if (activeSettings.IsWindowsDefault) await RestoreWindowsAsync();
            else await ApplyAsync();

            //Showhide the split mode help button
            if (!isWindows11 && activeSettings.IsDynamic)
            {
                splitHelpButton.IsVisible = true;
            }
            else
            {
                splitHelpButton.IsVisible = false;
            }

            if (activeSettings.IsNotFirstLaunch != true)
            {
                activeSettings.IsNotFirstLaunch = true;
                AboutWindow aw = new AboutWindow();
                aw.expander0.IsExpanded = true;
                Show();
                await aw.ShowDialog(this);
                try
                {
                    Show();
                }
                catch (InvalidOperationException)
                {

                }
                ShowMenuItem.Header = "Hide RoundedTB";
            }

            if (!activeSettings.IsWindowsDefault) AutoHide(true, taskbarDetails);

            UpdateUi();
            if (Environment.GetCommandLineArgs().Contains("--startup")) Hide();
            ShowMenuItem.Header = IsVisible ? "Hide RoundedTB" : "Show RoundedTB";

        }

        private async Task RefreshStartupRegistrationAsync()
        {
            StartupCheckBox.IsEnabled = false;
            try
            {
                StartupDiagnostics.Mark("startup-registration-query-start");
                var registered = await Task.Run(StartupRegistration.IsRegistered);
                StartupDiagnostics.Mark("startup-registration-query-complete");
                if (!exiting)
                {
                    StartupCheckBox.IsChecked = registered;
                    StartupRegistration.SetRestartRegistration(registered);
                }
            }
            catch (Exception error)
            {
                StartupDiagnostics.Mark("startup-registration-query-failed");
                interaction?.AddLog("Could not read startup registration: " + error);
            }
            finally { if (!exiting) StartupCheckBox.IsEnabled = true; }
        }

        public void UpdateUi()
        {
            SyncGeneralAutoHide();
            syncingMasterControl = true;
            try { enableRoundedTaskbarCheckBox.IsChecked = !activeSettings.IsWindowsDefault; }
            finally { syncingMasterControl = false; }
            enableRoundedTaskbarCheckBox.IsEnabled = !applying && !exiting;
            previewTaskbarBorder.Padding = activeSettings.IsWindowsDefault ? new Thickness(0) : new Thickness(8, 6);
            taskbarRectStandIn.CornerRadius = new CornerRadius(activeSettings.IsWindowsDefault ? 0 : 6);
            restoreWindowsButton.IsEnabled = !applying;
            restoreWindowsButton.Content = activeSettings.IsWindowsDefault ? "Enable RoundedTB" : "Restore Taskbar";
            if (!activeSettings.ShowTray && !activeSettings.DynamicTrayLayout.ShowOnHover)
            {
                trayRectStandIn.Opacity = 0.5;
            }
            else
            {
                trayRectStandIn.Opacity = 1;
            }

            if (!activeSettings.ShowWidgets && !activeSettings.DynamicWidgetsLayout.ShowOnHover)
            {
                widgetsRectStandIn.Opacity = 0.5;
            }
            else
            {
                widgetsRectStandIn.Opacity = 1;
            }

            if (!activeSettings.IsWindowsDefault && activeSettings.IsCentred && activeSettings.IsWindows11 && activeSettings.IsDynamic)
            {
                Grid.SetColumn(taskbarRectStandIn, 1);
                Grid.SetColumnSpan(taskbarRectStandIn, 1);
                trayRectStandIn.IsVisible = true;
                widgetsRectStandIn.IsVisible = true;
            }
            else if (!activeSettings.IsWindowsDefault && activeSettings.IsDynamic)
            {
                Grid.SetColumn(taskbarRectStandIn, 0);
                Grid.SetColumnSpan(taskbarRectStandIn, 2);
                trayRectStandIn.IsVisible = true;
                widgetsRectStandIn.IsVisible = false;
            }
            else
            {
                Grid.SetColumn(taskbarRectStandIn, 0);
                Grid.SetColumnSpan(taskbarRectStandIn, 3);
                trayRectStandIn.IsVisible = false;
                widgetsRectStandIn.IsVisible = false;

            }
        }

        private readonly object expandedWorkAreaLock = new();
        private readonly Dictionary<IntPtr, LocalPInvoke.RECT> originalWorkAreas = new();
        private bool workAreaExpansionApplied;
        private long lastWorkAreaCheck;

        private bool sessionNotificationsRegistered;
        internal volatile bool SessionLocked;

        internal void HandleSessionChange(int reason)
        {
            if (reason == 7) SessionLocked = true;
            else if (reason == 8 || reason == 15) SessionLocked = false;
            if (reason == 7 || reason == 8 || reason == 15)
                SessionResumeDiagnostics.Capture(this, reason);
        }

        internal void MaintainExpandedWorkAreas()
        {
            lock (expandedWorkAreaLock)
            {
                if (nativeTaskbarPin != null)
                {
                    if (!nativeTaskbarPin.Active && !exiting
                        && TaskbarPinController.FindTaskbar() != IntPtr.Zero
                        && System.Threading.Interlocked.CompareExchange(ref nativePinRefreshPending, 1, 0) == 0)
                        Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                nativeTaskbarPin?.Dispose();
                                nativeTaskbarPin = null;
                                nativePinAttempted = false;
                                nativePinRetryTimer?.Dispose();
                                nativePinRetryTimer = null;
                                nativePinRetryCount = 0;
                                expandedWorkAreaApplied = false;
                                if (!exiting && !activeSettings.IsWindowsDefault) AutoHide(true, taskbarDetails);
                            }
                            catch (Exception error) { interaction?.AddLog("Native taskbar restart failed: " + error.Message); }
                            finally { System.Threading.Interlocked.Exchange(ref nativePinRefreshPending, 0); }
                        });
                    return;
                }
                if (!workAreaExpansionApplied || Environment.TickCount64 - lastWorkAreaCheck < 500) return;
                lastWorkAreaCheck = Environment.TickCount64;
                foreach (var display in MonitorStuff.GetDisplays())
                {
                    var bounds = display.MonitorArea;
                    if (display.WorkArea.Left == bounds.Left && display.WorkArea.Top == bounds.Top
                        && display.WorkArea.Right == bounds.Right && display.WorkArea.Bottom == bounds.Bottom) continue;
                    originalWorkAreas.TryAdd(display.Handle, display.WorkArea);
                    bool expanded = LocalPInvoke.SystemParametersInfo(0x002F, 0, ref bounds, 0);
                    SessionResumeDiagnostics.RecordWrite("ExpandWorkArea", IntPtr.Zero, bounds, expanded ? 1 : 0);
                    if (expanded)
                        LocalPInvoke.EnumWindows((window, _) =>
                        {
                            var placement = LocalPInvoke.WINDOWPLACEMENT.Default;
                            if (LocalPInvoke.IsWindowVisible(window)
                                && LocalPInvoke.GetWindowPlacement(window, ref placement)
                                && MaximizedWindowTaskbarAccess.Eligible(LocalPInvoke.GetWindowStyleBits(window, -16), placement.ShowCmd)
                                && LocalPInvoke.MonitorFromWindow(window, 2) == display.Handle
                                && LocalPInvoke.GetWindowRect(window, out var outer)
                                && LocalPInvoke.DwmGetWindowFrameBounds(window, LocalPInvoke.DWMWINDOWATTRIBUTE.ExtendedFrameBounds, out var frame, 16) == 0)
                            {
                                int left = bounds.Left - (frame.Left - outer.Left);
                                int top = bounds.Top - (frame.Top - outer.Top);
                                int right = bounds.Right + (outer.Right - frame.Right);
                                int bottom = bounds.Bottom + (outer.Bottom - frame.Bottom);
                                if (outer.Left != left || outer.Top != top || outer.Right != right || outer.Bottom != bottom)
                                    LocalPInvoke.SetWindowPos(window, IntPtr.Zero, left, top, right - left, bottom - top,
                                        LocalPInvoke.SetWindowPosFlags.IgnoreZOrder | LocalPInvoke.SetWindowPosFlags.DoNotActivate
                                        | LocalPInvoke.SetWindowPosFlags.AsynchronousWindowPosition);
                            }
                            return true;
                        }, IntPtr.Zero);
                }
            }
        }

        private bool autoHideApplied;
        private NativeTaskbarPin nativeTaskbarPin;
        private bool nativePinAttempted;
        private int nativePinRefreshPending;
        private IDisposable nativePinRetryTimer;
        private int nativePinRetryCount;

        private void RetryNativeAttachment()
        {
            if (nativePinRetryTimer != null || nativePinRetryCount >= 4) return;
            int delay = 1 << nativePinRetryCount++;
            nativePinRetryTimer = DispatcherTimer.RunOnce(() =>
            {
                nativePinRetryTimer = null;
                if (exiting || activeSettings.IsWindowsDefault || !WorkAreaExpansion.Enabled(activeSettings)) return;
                nativePinAttempted = false;
                AutoHide(true, taskbarDetails);
            }, TimeSpan.FromSeconds(delay));
        }

        private bool expandedWorkAreaApplied;
        private volatile Dictionary<IntPtr, LocalPInvoke.RECT> expandedWorkAreas = new();
        internal IReadOnlyDictionary<IntPtr, LocalPInvoke.RECT> ExpandedWorkAreas => expandedWorkAreas;

        public void AutoHide(bool enabled, List<Types.Taskbar> taskbarDetails)
        {
            if (!OperatingSystem.IsWindows() || taskbarDetails.Count == 0) return;
            bool hide = false;
            bool expand = enabled && WorkAreaExpansion.Enabled(activeSettings);
            if (!expand)
            {
                nativePinAttempted = false;
                nativePinRetryTimer?.Dispose();
                nativePinRetryTimer = null;
                nativePinRetryCount = 0;
            }
            if (!expand && nativeTaskbarPin != null)
            {
                lock (expandedWorkAreaLock)
                {
                    nativeTaskbarPin.Dispose();
                    nativeTaskbarPin = null;
                }
            }
            if (expand)
            {
                lock (expandedWorkAreaLock)
                {
                    if (nativeTaskbarPin == null && !nativePinAttempted)
                    {
                        nativePinAttempted = true;
                        nativeTaskbarPin = NativeTaskbarPin.TryStart(message => interaction?.AddLog(message),
                            () => Dispatcher.UIThread.Post(() =>
                            {
                                if (exiting || activeSettings.IsWindowsDefault || !WorkAreaExpansion.Enabled(activeSettings)) return;
                                nativePinAttempted = false;
                                AutoHide(true, taskbarDetails);
                            }), RetryNativeAttachment);
                    }
                    if (nativeTaskbarPin?.Active == true)
                    {
                        nativePinRetryTimer?.Dispose();
                        nativePinRetryTimer = null;
                        nativePinRetryCount = 0;
                        bool changed = !autoHideApplied || !expandedWorkAreaApplied;
                        // Native auto-hide releases the reservation; the helper
                        // prevents retraction and our regions control visibility.
                        workAreaExpansionApplied = false;
                        originalWorkAreas.Clear();
                        expandedWorkAreas = MonitorStuff.GetDisplays().ToDictionary(display => display.Handle, display => display.MonitorArea);
                        autoHideApplied = true;
                        expandedWorkAreaApplied = true;
                        if (changed) interaction?.AddLog("Work area mode: native reservation released, taskbar pinned open.");
                        return;
                    }
                    nativeTaskbarPin?.Dispose();
                    nativeTaskbarPin = null;
                }
            }
            if (hide == autoHideApplied && expand == expandedWorkAreaApplied) return;
            lock (expandedWorkAreaLock)
            {
                workAreaExpansionApplied = false;
                foreach (var original in originalWorkAreas.Values)
                {
                    var area = original;
                    bool restored = LocalPInvoke.SystemParametersInfo(0x002F, 0, ref area, 2);
                    SessionResumeDiagnostics.RecordWrite("RestoreWorkArea", IntPtr.Zero, area, restored ? 1 : 0);
                }
                originalWorkAreas.Clear();
            }
            var displays = MonitorStuff.GetDisplays();
            if (hide != autoHideApplied || expand != expandedWorkAreaApplied)
                foreach (var taskbar in taskbarDetails)
                {
                    LocalPInvoke.SetWindowPos(taskbar.TaskbarHwnd, new IntPtr(-1), 0, 0, 0, 0,
                        LocalPInvoke.SetWindowPosFlags.IgnoreMove | LocalPInvoke.SetWindowPosFlags.IgnoreResize);
                    Taskbar.SetTaskbarState(LocalPInvoke.AppBarStates.AlwaysOnTop, taskbar.TaskbarHwnd);
                }
            if (expand)
            {
                lock (expandedWorkAreaLock)
                {
                    // A force-stopped previous instance can leave a full work area.
                    // Capture the native reservation from the actual taskbar height
                    // in that case so disabling or exiting still restores Windows.
                    foreach (var display in MonitorStuff.GetDisplays())
                    {
                        var original = display.WorkArea;
                        if (original.Bottom == display.MonitorArea.Bottom)
                            foreach (var taskbar in taskbarDetails)
                                if (LocalPInvoke.MonitorFromWindow(taskbar.TaskbarHwnd, 2) == display.Handle
                                    && LocalPInvoke.GetWindowRect(taskbar.TaskbarHwnd, out var rect)
                                    && rect.Right - rect.Left >= display.MonitorArea.Right - display.MonitorArea.Left
                                    && rect.Bottom - rect.Top > 2 && rect.Bottom - rect.Top < (display.MonitorArea.Bottom - display.MonitorArea.Top) / 2)
                                    original.Bottom = display.MonitorArea.Bottom - (rect.Bottom - rect.Top);
                        originalWorkAreas[display.Handle] = original;
                    }
                    workAreaExpansionApplied = true;
                    lastWorkAreaCheck = 0;
                }
                MaintainExpandedWorkAreas();
            }
            var expandedAreas = new Dictionary<IntPtr, LocalPInvoke.RECT>();
            if (expand)
            {
                foreach (var display in displays) expandedAreas[display.Handle] = display.MonitorArea;
            }
            expandedWorkAreas = expandedAreas;
            interaction?.AddLog($"Work area mode: auto-hide={hide}, expanded={expand}, monitors={expandedAreas.Count}.");
            autoHideApplied = hide;
            expandedWorkAreaApplied = expand;
        }

        private NativeMenuItem enableRoundedTbMenuItem;

        private void UpdateTrayEnabledState()
        {
            if (enableRoundedTbMenuItem == null) return;
            enableRoundedTbMenuItem.IsChecked = !activeSettings.IsWindowsDefault;
            enableRoundedTbMenuItem.IsEnabled = initialized && !applying && !exiting;
        }

        internal void CreateTrayIcon()
        {
            enableRoundedTbMenuItem = new NativeMenuItem("Enable RoundedTB") { ToggleType = MenuItemToggleType.CheckBox };
            UpdateTrayEnabledState();
            enableRoundedTbMenuItem.Click += async (_, _) =>
            {
                if (!initialized || applying || exiting) { UpdateTrayEnabledState(); return; }
                enableRoundedTbMenuItem.IsEnabled = false;
                try
                {
                    if (activeSettings.IsWindowsDefault) { dynamicCheckBox.IsChecked = true; await ApplyAsync(); }
                    else await RestoreWindowsAsync();
                }
                catch (Exception error) { interaction?.AddLog(error.ToString()); UpdateUi(); }
                finally { UpdateTrayEnabledState(); }
            };
            var startup = new NativeMenuItem("Run at startup") { ToggleType = MenuItemToggleType.CheckBox };
            startup.IsChecked = StartupCheckBox.IsChecked == true;
            startup.Click += async (_, _) =>
            {
                try { await ToggleStartupAsync(); }
                catch (Exception error) { interaction?.AddLog(error.ToString()); }
                startup.IsChecked = StartupCheckBox.IsChecked == true;
                startup.IsEnabled = StartupCheckBox.IsEnabled;
            };
            ShowMenuItem.Click += (_, _) => ShowMenuItem_Click(null, null);
            var close = new NativeMenuItem("Close RoundedTB");
            close.Click += (_, _) => Exit();
            var menu = new NativeMenu();
            menu.NeedsUpdate += async (_, _) =>
            {
                UpdateTrayEnabledState();
                try { if (IsRunningAsUWP()) await StartupInit(false); }
                catch (Exception error) { interaction?.AddLog(error.ToString()); }
                startup.IsChecked = StartupCheckBox.IsChecked == true;
                startup.IsEnabled = StartupCheckBox.IsEnabled;
            };
            menu.Items.Add(enableRoundedTbMenuItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(startup);
            menu.Items.Add(ShowMenuItem);
            menu.Items.Add(close);
            trayIcon = new TrayIcon { Icon = Icon, ToolTipText = "RoundedTB", Menu = menu, IsVisible = true };
            trayIcon.Clicked += (_, _) => ShowMenuItem_Click(null, null);
            var icons = TrayIcon.GetIcons(Application.Current);
            if (icons == null)
            {
                icons = new TrayIcons();
                TrayIcon.SetIcons(Application.Current, icons);
            }
            icons.Add(trayIcon);
        }

        internal void ToggleTrayVisibility()
        {
            trayVisibilityCombo.SelectedIndex = activeSettings.ShowTray || activeSettings.DynamicTrayLayout.ShowOnHover ? 2 : 0;
        }

        private bool syncingMasterControl;

        private async void EnableRoundedTaskbar_Changed(object sender, RoutedEventArgs e)
        {
            if (!initialized || syncingMasterControl || exiting || applying) return;
            bool enable = enableRoundedTaskbarCheckBox.IsChecked == true;
            if (enable == !activeSettings.IsWindowsDefault) return;
            try
            {
                if (enable) { dynamicCheckBox.IsChecked = true; await ApplyAsync(); }
                else await RestoreWindowsAsync();
            }
            catch (Exception error) { interaction?.AddLog(error.ToString()); UpdateUi(); }
        }

        private async void RestoreWindows_Click(object sender, RoutedEventArgs e)
        {
            try { if (activeSettings.IsWindowsDefault) { dynamicCheckBox.IsChecked = true; await ApplyAsync(); } else await RestoreWindowsAsync(); }
            catch (Exception error) { interaction?.AddLog(error.ToString()); }
        }

        internal async Task RestoreWindowsAsync()
        {
            if (!initialized || exiting || applying) return;
            applying = true;
            restoreWindowsButton.IsEnabled = false;
            try
            {
                if (taskbarThread.WorkerSupportsCancellation) taskbarThread.CancelAsync();
                while (taskbarThread.IsBusy) await Task.Delay(25);
                foreach (var taskbar in taskbarDetails)
                {
                    Taskbar.ResetTaskbar(taskbar, activeSettings);
                    taskbar.Ignored = true;
                    taskbar.TaskbarHidden = false;
                }
                AutoHide(false, taskbarDetails);
                activeSettings.IsWindowsDefault = true;
                interaction?.WriteJSON();
            }
            finally
            {
                applying = false;
                restoreWindowsButton.IsEnabled = true;
                UpdateUi();
            }
        }

        private async Task ApplyAsync()
        {
            if (!initialized || exiting || applying) return;
            applying = true;
            restoreWindowsButton.IsEnabled = false;
            try
            {
                mTopInput_LostFocus(null, null);
                mBottomInput_LostFocus(null, null);
                mLeftInput_LostFocus(null, null);
                mRightInput_LostFocus(null, null);
                cornerRadiusInput_LostFocus(null, null);
                int mt = 0;
                int ml = 0;
                int mb = 0;
                int mr = 0;



                {
                    if ((!int.TryParse(mTopInput.Text, out mt) && mTopInput.Text != string.Empty)
                    || (!int.TryParse(mLeftInput.Text, out ml) && mLeftInput.Text != string.Empty)
                    || (!int.TryParse(mBottomInput.Text, out mb) && mBottomInput.Text != string.Empty)
                    || (!int.TryParse(mRightInput.Text, out mr) && mRightInput.Text != string.Empty))
                    {
                        return;
                    }
                }

                if (taskbarThread.IsBusy)
                {
                    taskbarThread.CancelAsync();
                    while (taskbarThread.IsBusy) await Task.Delay(25);
                }
                activeSettings.IsWindowsDefault = false;
                activeSettings.AutoHide = 0;
                activeSettings.RevealOnAppOpen = revealOnAppOpenCheckBox.IsChecked == true;
                activeSettings.AppOpenRevealSeconds = (double)(appOpenDurationInput.Value ?? 1);
                activeSettings.RevealOnAppAttention = revealOnAppAttentionCheckBox.IsChecked == true;
                activeSettings.KeepAppAttentionOpenUntilAccepted = keepAppAttentionOpenCheckBox.IsChecked == true;
                activeSettings.AppAttentionRevealSeconds = (double)(appAttentionDurationInput.Value ?? 5);
                activeSettings.ExpandMaximizedWorkArea = expandWorkAreaCheckBox.IsChecked == true;
                activeSettings.IsDynamic = (bool)dynamicCheckBox.IsChecked;
                activeSettings.IsCentred = Taskbar.CheckIfCentred();
                activeSettings.ShowTray = (bool)showTrayCheckBox.IsChecked;
                activeSettings.ShowWidgets = (bool)showWidgetsCheckBox.IsChecked;
                activeSettings.FillOnMaximise = !activeSettings.ExpandMaximizedWorkArea && fillMaximisedCheckBox.IsChecked == true;
                activeSettings.FillOnTaskSwitch = (bool)fillAltTabCheckBox.IsChecked;
                // Per-segment hover preferences are updated directly by the editor.

                try
                {
                    foreach (Types.Taskbar taskbar in taskbarDetails)
                    {
                        // Apply must honor the current window state, even when the worker
                        // already marked this taskbar as restored on an earlier pass.
                        bool restoreRequested = Taskbar.TaskbarShouldBeFilled(taskbar.TaskbarHwnd, activeSettings);
                        taskbar.RevealAllSegments = restoreRequested && WorkAreaExpansion.Enabled(activeSettings) && !activeSettings.IsWindowsDefault;
                        if (restoreRequested && !taskbar.RevealAllSegments)
                        {
                            Taskbar.ResetTaskbar(taskbar, activeSettings, removeLayeredStyle: false);
                            taskbar.AppearanceRestored = true;
                            taskbar.Ignored = true;
                            taskbar.TaskbarHidden = false;
                            continue;
                        }
                        taskbar.AppearanceRestored = false;
                        taskbar.Ignored = false;
                        int isFullTest = taskbar.TrayRect.Left - taskbar.AppListRect.Right;
                        if (!activeSettings.IsDynamic || (isFullTest <= taskbar.ScaleFactor * 25 && isFullTest > 0 && taskbar.TrayRect.Left != 0))
                        {
                            Taskbar.UpdateSimpleTaskbar(taskbar, activeSettings);
                        }
                        else
                        {
                            Taskbar.UpdateDynamicTaskbar(taskbar, activeSettings);
                        }
                    }
                }
                catch (InvalidOperationException aaaa)
                {
                    interaction.AddLog(aaaa.Message);
                }


                if (taskbarThread.IsBusy == false)
                {
                    taskbarThread.RunWorkerAsync((mt, ml, mb, mr, 0));
                }
                else
                {
                    taskbarThread.CancelAsync();
                    while (taskbarThread.IsBusy == true)
                    {
                        await Task.Delay(25);
                    }
                    if (!exiting) taskbarThread.RunWorkerAsync((mt, ml, mb, mr, 0));
                }

                if (exiting) return;
                AutoHide(true, taskbarDetails);
                interaction.WriteJSON();
                UpdateUi();

            }
            finally
            {
                applying = false;
                restoreWindowsButton.IsEnabled = true;
                UpdateUi();
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (!shouldReallyDieNoReally)
            {
                e.Cancel = true;
                foreach (var window in OwnedWindows.ToArray()) window.Close();
                Hide();
                ShowMenuItem.Header = "Show RoundedTB";
            }
            base.OnClosing(e);
        }

        public async void Exit()
        {
            if (exiting) return;
            exiting = true;
            try
            {
                while (applying) await Task.Delay(25);
                if (taskbarThread.WorkerSupportsCancellation) taskbarThread.CancelAsync();
                while (taskbarThread.IsBusy) await Task.Delay(25);
                background?.CloseStripSurfaces();
                var restoreTaskbars = taskbarDetails.ToList();
                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        // Explorer may have replaced a taskbar handle since the
                        // last frame. Restore both cached and current taskbars.
                        foreach (var taskbar in Taskbar.GenerateTaskbarInfo(enableEffects: false))
                            if (!restoreTaskbars.Any(existing => existing.TaskbarHwnd == taskbar.TaskbarHwnd))
                                restoreTaskbars.Add(taskbar);
                    }
                    catch (Exception error) { interaction?.AddLog(error.ToString()); }
                }
                foreach (var taskbar in restoreTaskbars)
                {
                    if (taskbar.TaskbarHwnd == IntPtr.Zero) continue;
                    try { Taskbar.ResetTaskbar(taskbar, activeSettings); }
                    catch (Exception error) { interaction?.AddLog(error.ToString()); }
                }
                if (autoHideApplied || expandedWorkAreaApplied) AutoHide(false, restoreTaskbars);
                if (initialized) interaction?.WriteJSON();
                interaction?.AddLog("Exiting RoundedTB.");
            }
            catch (Exception error) { interaction?.AddLog(error.ToString()); }
            finally
            {
                StopAttentionTest();
                appAttentionReveal.Clear();
                if (OperatingSystem.IsWindows()) Win32Properties.RemoveWndProcHookCallback(this, TaskbarButtonHook);
                if (shellHookRegistered) LocalPInvoke.DeregisterShellHookWindow(NativeHandle);
                if (sessionNotificationsRegistered) LocalPInvoke.WTSUnRegisterSessionNotification(NativeHandle);
                if (NativeHandle != IntPtr.Zero) LocalPInvoke.UnregisterHotKey(NativeHandle, 9000);
                if (interaction != null) Win32Properties.RemoveWndProcHookCallback(this, interaction.HwndHook);
                if (trayIcon != null)
                {
                    TrayIcon.GetIcons(Application.Current)?.Remove(trayIcon);
                    trayIcon.Dispose();
                }
                foreach (var window in OwnedWindows.ToArray()) window.Close();
                shouldReallyDieNoReally = true;
                Close();
            }
        }

        public void ShowMenuItem_Click(object sender, EventArgs e)
        {
            if (!IsVisible)
            {
                Show();
                Activate();
                ShowMenuItem.Header = "Hide RoundedTB";
            }
            else
            {
                foreach (var window in OwnedWindows.ToArray()) window.Close();
                Hide();
                ShowMenuItem.Header = "Show RoundedTB";
            }
        }

        private async void Startup_Clicked(object sender, RoutedEventArgs e)
        {
            try { await ToggleStartupAsync(); }
            catch (Exception error) { interaction?.AddLog(error.ToString()); }
        }

        private async Task ToggleStartupAsync()
        {
            Debug.WriteLine("Startup toggled");
            if (IsRunningAsUWP())
            {
                await StartupToggle();
                await StartupInit(false);
            }
            else
            {
                if (StartupRegistration.IsRegistered())
                {
                    StartupRegistration.Disable();
                    StartupCheckBox.IsChecked = false;
                }
                else
                {
                    EnableStartup();
                    StartupCheckBox.IsChecked = true;
                }
            }
        }

        public void EnableStartup() => StartupRegistration.Enable();

        async Task StartupToggle()
        {
            StartupTask startupTask = await StartupTask.GetAsync("RTB"); // Pass the task ID you specified in the appxmanifest file
            switch (startupTask.State)
            {
                case StartupTaskState.Disabled:
                    StartupTaskState newState = await startupTask.RequestEnableAsync();
                    StartupCheckBox.IsEnabled = true;
                    break;

                case StartupTaskState.DisabledByUser:
                    StartupCheckBox.IsEnabled = false;
                    break;

                case StartupTaskState.EnabledByPolicy:
                    StartupCheckBox.IsEnabled = false;
                    break;

                case StartupTaskState.DisabledByPolicy:
                    StartupCheckBox.IsEnabled = false;
                    break;

                case StartupTaskState.Enabled:
                    startupTask.Disable();
                    StartupCheckBox.IsEnabled = true;
                    break;
            }
        }

        async Task StartupInit(bool clean)
        {
            StartupTask startupTask = await StartupTask.GetAsync("RTB");
            switch (startupTask.State)
            {
                case StartupTaskState.Disabled:
                    StartupCheckBox.IsChecked = false;
                    StartupCheckBox.IsEnabled = true;
                    if (clean)
                    {
                        Show();
                        ShowMenuItem.Header = "Hide RoundedTB";
                    }
                    StartupCheckBox.Content = "Launch RoundedTB at Windows sign-in";
                    break;

                case StartupTaskState.DisabledByUser:
                    StartupCheckBox.IsChecked = false;
                    StartupCheckBox.IsEnabled = false;
                    if (clean)
                    {
                        Show();
                        ShowMenuItem.Header = "Hide RoundedTB";
                    }
                    StartupCheckBox.Content = "Startup unavailable";
                    break;

                case StartupTaskState.EnabledByPolicy:
                    StartupCheckBox.IsChecked = true;
                    StartupCheckBox.IsEnabled = false;
                    if (clean)
                    {
                        Hide();
                        ShowMenuItem.Header = "Show RoundedTB";
                    }
                    StartupCheckBox.Content = "Startup mandatory";
                    break;

                case StartupTaskState.DisabledByPolicy:
                    StartupCheckBox.IsChecked = false;
                    StartupCheckBox.IsEnabled = false;
                    if (clean)
                    {
                        Show();
                        ShowMenuItem.Header = "Hide RoundedTB";
                    }
                    StartupCheckBox.Content = "Startup unavailable";
                    break;

                case StartupTaskState.Enabled:
                    StartupCheckBox.IsChecked = true;
                    StartupCheckBox.IsEnabled = true;
                    if (clean)
                    {
                        Hide();
                        ShowMenuItem.Header = "Show RoundedTB";
                    }
                    StartupCheckBox.Content = "Launch RoundedTB at Windows sign-in";
                    break;
            }
        }

        // Checks if running as a UWP app
        public bool IsRunningAsUWP()
        {
            try
            {
                _ = Windows.ApplicationModel.Package.Current;
                return true;
            }
            catch (Exception)
            {
                return false;
            }

        }

        private void UpdateSettingsNavigation(double width)
        {
            const double cardWidth = 610;
            const double pageInsets = 56;
            const double navigationWidth = 185;
            bool showNavigation = width >= cardWidth + pageInsets + navigationWidth;
            settingsNavigation.IsVisible = showNavigation;
            settingsContentGrid.ColumnDefinitions[0].Width = new GridLength(showNavigation ? 165 : 0);
            settingsContentGrid.ColumnSpacing = showNavigation ? 20 : 0;
            bool monitorBeside = width >= 760;
            monitorSettingsGrid.ColumnDefinitions[1].Width = monitorBeside ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(monitorSettingsControls, monitorBeside ? 1 : 0);
            Grid.SetRow(monitorSettingsControls, monitorBeside ? 0 : 1);
            bool beside = width - pageInsets - (showNavigation ? navigationWidth : 0) >= cardWidth;
            areaSettingsGrid.ColumnDefinitions[0].Width = beside ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            areaSettingsGrid.ColumnDefinitions[1].Width = beside ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(shapeSpacingCard, beside ? 1 : 0);
            Grid.SetRow(shapeSpacingCard, beside ? 0 : 1);
        }

        private void SettingsSection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.CommandParameter is not string sectionName) return;
            foreach (var navigation in new[] { generalNavigationButton, appearanceNavigationButton, notificationsNavigationButton, windowsNavigationButton, advancedNavigationButton })
                navigation.Classes.Set("nav-selected", navigation == button);
            if (sectionName == "notificationsSection") { CommitEditor(); LoadEditor(activeSettings.IsDynamic ? 1 : 0); }
            if (sectionName == "generalSection") settingsScrollViewer.Offset = default;
            else this.FindControl<Control>(sectionName)?.BringIntoView(new Rect(0, 0, 1, 1));
        }

        private void dynamicCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            CommitEditor();
            activeSettings.IsDynamic = true;
            LoadEditor(1);
            UpdateUi();
            centredCheckBox.IsEnabled = true;
            showSegmentsOnHoverCheckBox.IsEnabled = true;
            showTrayCheckBox.IsEnabled = !activeSettings.DynamicTrayLayout.ShowOnHover;
            PreviewEditorChange();
            if (!isWindows11)
            {
                splitHelpButton.IsVisible = true;
                if (IsVisible)
                {
                    splitHelpButton_Click(null, null);
                }
            }

        }

        private void dynamicCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {

            CommitEditor();
            activeSettings.IsDynamic = false;
            LoadEditor(0);
            UpdateUi();
            centredCheckBox.IsEnabled = false;
            centredCheckBox.IsChecked = false;
            showSegmentsOnHoverCheckBox.IsEnabled = true;
            showTrayCheckBox.IsEnabled = false;
            PreviewEditorChange();
            if (!isWindows11)
            {
                splitHelpButton.IsVisible = false;
            }
        }

        private void NumericInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not NumericUpDown input) return;
            int step = e.Key switch { Key.Up or Key.Right => 1, Key.Down or Key.Left => -1, _ => 0 };
            if (step == 0) return;
            input.Value = Math.Clamp((input.Value ?? 0) + step * input.Increment, input.Minimum, input.Maximum);
            e.Handled = true;
        }

        private void NumericInput_ValueChanged(object sender, NumericUpDownValueChangedEventArgs e)
        {
            if (!initialized || loadingEditor || SelectedSettings == null
                || sender is not NumericUpDown input || input.Value is not decimal number
                || number < int.MinValue || number > int.MaxValue) return;
            int value = (int)number;
            if (input == mTopInput) SelectedSettings.MarginTop = value;
            else if (input == mBottomInput) SelectedSettings.MarginBottom = value;
            else if (input == mLeftInput) SelectedSettings.MarginLeft = value;
            else if (input == mRightInput) SelectedSettings.MarginRight = value;
            else if (input == cornerRadiusInput)
            {
                SelectedSettings.CornerRadius = value;
            }
            PreviewEditorChange();
        }

        private bool syncingAutoHide;

        private void SyncGeneralAutoHide()
        {
            if (syncingAutoHide) return;
            syncingAutoHide = true;
            try
            {
                autoHideTaskbarCheckBox.IsChecked = SegmentHover.AppSettings(activeSettings)?.ShowOnHover == true;
                autoHideTrayCheckBox.IsChecked = activeSettings.DynamicTrayLayout?.ShowOnHover == true;
                autoHideWidgetsCheckBox.IsChecked = activeSettings.DynamicWidgetsLayout?.ShowOnHover == true;
                autoHideTrayCheckBox.IsEnabled = autoHideWidgetsCheckBox.IsEnabled = activeSettings.IsDynamic;
                appVisibilityLabel.Text = activeSettings.IsDynamic ? "Apps" : "Entire taskbar";
                appVisibilityCombo.SelectedIndex = SegmentHover.AppSettings(activeSettings)?.ShowOnHover == true ? 1 : 0;
                trayVisibilityCombo.SelectedIndex = activeSettings.DynamicTrayLayout.ShowOnHover ? 1 : activeSettings.ShowTray ? 0 : 2;
                widgetsVisibilityCombo.SelectedIndex = activeSettings.DynamicWidgetsLayout.ShowOnHover ? 1 : activeSettings.ShowWidgets ? 0 : 2;
                trayVisibilityCombo.IsEnabled = widgetsVisibilityCombo.IsEnabled = activeSettings.IsDynamic;
                showTrayCheckBox.IsChecked = activeSettings.ShowTray;
                showWidgetsCheckBox.IsChecked = activeSettings.ShowWidgets;
                taskbarRectStandIn.Content = "Taskbar";
                autoHideSegmentsHint.Text = activeSettings.IsDynamic
                    ? "Each area hides separately. Set its hover target, hide delay, and animation in Shape & hover."
                    : "The entire taskbar hides together. Enable separate areas to control widgets and the system tray independently.";
                autoHideSegmentsHint.IsVisible = !activeSettings.IsDynamic;
                RefreshVisibilityButtons();
                RefreshSettingAvailability();
            }
            finally { syncingAutoHide = false; }
        }

        private void RefreshVisibilityButtons()
        {
            bool separateArea = selectedSegment is 2 or 3;
            bool hidden = selectedSegment == 2 ? !activeSettings.ShowTray : selectedSegment == 3 && !activeSettings.ShowWidgets;
            int mode = SelectedSettings?.ShowOnHover == true ? 1 : separateArea && hidden ? 2 : 0;
            visibilityAlwaysButton.Classes.Set("selected", mode == 0);
            visibilityHoverButton.Classes.Set("selected", mode == 1);
            visibilityHiddenButton.Classes.Set("selected", mode == 2);
            visibilityHiddenButton.IsVisible = separateArea;
            visibilityButtonsGrid.ColumnDefinitions[2].Width = separateArea
                ? new GridLength(96) : new GridLength(0);
            int maximizedMode = (int)(SelectedSettings?.MaximizedVisibility ?? (Types.VisibilityMode)mode);
            maximizedVisibilityAlwaysButton.Classes.Set("selected", maximizedMode == 0);
            maximizedVisibilityHoverButton.Classes.Set("selected", maximizedMode == 1);
            maximizedVisibilityHiddenButton.Classes.Set("selected", maximizedMode == 2);
            maximizedVisibilityHiddenButton.IsVisible = separateArea;
            maximizedVisibilityButtonsGrid.ColumnDefinitions[2].Width = separateArea
                ? new GridLength(96) : new GridLength(0);
        }

        private void MaximizedVisibilityButton_Click(object sender, RoutedEventArgs e)
        {
            if (!initialized || loadingEditor || exiting || sender is not Button button
                || !int.TryParse(button.CommandParameter?.ToString(), out int mode)) return;
            SelectedSettings.MaximizedVisibility = (Types.VisibilityMode)mode;
            PreviewEditorChange();
            UpdateUi();
        }

        private void VisibilityButton_Click(object sender, RoutedEventArgs e)
        {
            if (!initialized || loadingEditor || exiting || sender is not Button button
                || !int.TryParse(button.CommandParameter?.ToString(), out int mode)) return;
            var combo = selectedSegment switch { 2 => trayVisibilityCombo, 3 => widgetsVisibilityCombo, _ => appVisibilityCombo };
            combo.SelectedIndex = mode;
        }

        private void VisibilityMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!initialized || syncingAutoHide || loadingEditor || sender is not ComboBox combo || combo.SelectedIndex < 0) return;
            int mode = combo.SelectedIndex;
            if (combo == appVisibilityCombo) SegmentHover.AppSettings(activeSettings).ShowOnHover = mode == 1;
            else if (combo == trayVisibilityCombo)
            {
                activeSettings.DynamicTrayLayout.ShowOnHover = mode == 1;
                activeSettings.ShowTray = mode != 2;
            }
            else if (combo == widgetsVisibilityCombo)
            {
                activeSettings.DynamicWidgetsLayout.ShowOnHover = mode == 1;
                activeSettings.ShowWidgets = mode != 2;
            }
            LoadEditor(selectedSegment);
            PreviewEditorChange();
            UpdateUi();
        }

        private void GeneralAutoHide_Changed(object sender, RoutedEventArgs e)
        {
            if (!initialized || syncingAutoHide || loadingEditor) return;
            bool hide = (sender as CheckBox)?.IsChecked == true;
            if (sender == autoHideTaskbarCheckBox) SegmentHover.AppSettings(activeSettings).ShowOnHover = hide;
            else if (sender == autoHideTrayCheckBox)
            {
                activeSettings.DynamicTrayLayout.ShowOnHover = hide;
                activeSettings.ShowTray = true;
                showTrayCheckBox.IsChecked = true;
            }
            else if (sender == autoHideWidgetsCheckBox)
            {
                activeSettings.DynamicWidgetsLayout.ShowOnHover = hide;
                activeSettings.ShowWidgets = true;
                showWidgetsCheckBox.IsChecked = true;
            }
            LoadEditor(selectedSegment);
            PreviewEditorChange();
            UpdateUi();
        }

        private void RefreshSettingAvailability()
        {
            if (!initialized) return;
            bool hide = WindowModeVisibility.AnyAppAutoHide(activeSettings);
            revealOnAppOpenCheckBox.Content = activeSettings.IsDynamic ? "Show when a window opens" : "Show when a window opens";
            if (revealOnAppAttentionCheckBox.Content is TextBlock attentionLabel)
                attentionLabel.Text = activeSettings.IsDynamic ? "Show when an app flashes" : "Show when an app flashes";
            revealOnAppAttentionCheckBox.IsEnabled = hide;
            testAppAttentionButton.IsEnabled = hide && revealOnAppAttentionCheckBox.IsChecked == true
                && AppAttentionReveal.Enabled(activeSettings) && shellHookRegistered && !testingAppAttention;
            keepAppAttentionOpenCheckBox.IsEnabled = hide && revealOnAppAttentionCheckBox.IsChecked == true;
            appAttentionDurationPanel.IsEnabled = hide && revealOnAppAttentionCheckBox.IsChecked == true
                && keepAppAttentionOpenCheckBox.IsChecked != true;
            revealOnAppOpenCheckBox.IsEnabled = hide;
            appOpenDurationPanel.IsEnabled = hide && revealOnAppOpenCheckBox.IsChecked == true;
            appOpenHint.Text = hide
                ? activeSettings.IsDynamic ? "Shows app icons on the screen where the window opens. Hover over them to keep them visible."
                    : "Shows the whole taskbar on the screen where the window opens. Hover over it to keep it visible."
                : "Choose Auto-hide for " + (activeSettings.IsDynamic ? "Apps" : "Entire taskbar") + " in Areas to use this option.";
            appOpenHint.IsVisible = !hide;
            bool expanded = expandWorkAreaCheckBox.IsChecked == true;
            fillMaximisedCheckBox.IsEnabled = !expanded;
            restorationHeading.Text = expanded ? "Show enabled areas, keeping their shape." : "Restore the full Windows taskbar.";
            bool showWindowSpace = expanded && !activeSettings.IsWindowsDefault;
            previewReclaimedSpace.IsVisible = showWindowSpace;
            previewTaskbarBorder.Background = showWindowSpace ? Avalonia.Media.Brushes.Transparent
                : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#30000000"));
            UpdateMasterControl();
        }

        private void UpdateMasterControl()
        {
            UpdateTrayEnabledState();
            restoreWindowsButton.Content = activeSettings.IsWindowsDefault ? "Enable RoundedTB" : "Restore Taskbar";
            restoreWindowsButton.IsEnabled = !applying && !exiting;
            enableRoundedTaskbarCheckBox.IsEnabled = !applying && !exiting;
        }

        private void ApplyLiveSettings()
        {
            if (!initialized || loadingEditor || exiting) return;
            activeSettings.RevealOnAppOpen = revealOnAppOpenCheckBox.IsChecked == true;
            activeSettings.AppOpenRevealSeconds = (double)(appOpenDurationInput.Value ?? 1);
            activeSettings.RevealOnAppAttention = revealOnAppAttentionCheckBox.IsChecked == true;
            activeSettings.KeepAppAttentionOpenUntilAccepted = keepAppAttentionOpenCheckBox.IsChecked == true;
            activeSettings.AppAttentionRevealSeconds = (double)(appAttentionDurationInput.Value ?? 5);
            if (expandWorkAreaCheckBox.IsChecked == true && fillMaximisedCheckBox.IsChecked == true)
            {
                loadingEditor = true;
                try { fillMaximisedCheckBox.IsChecked = false; }
                finally { loadingEditor = false; }
            }
            activeSettings.FillOnMaximise = expandWorkAreaCheckBox.IsChecked != true && fillMaximisedCheckBox.IsChecked == true;
            activeSettings.FillOnTaskSwitch = fillAltTabCheckBox.IsChecked == true;
            activeSettings.ExpandMaximizedWorkArea = expandWorkAreaCheckBox.IsChecked == true;
            PreviewEditorChange();
            RefreshSettingAvailability();
        }

        private void RefreshHoverOptions()
        {
            hoverOptionsPanel.IsVisible = true;
            bool anyHover = SelectedSettings?.ShowOnHover == true || SelectedSettings?.MaximizedVisibility == Types.VisibilityMode.AutoHide;
            hoverOptionsPanel.IsEnabled = anyHover;
            hoverPillPanel.IsVisible = selectedSegment is 1 or 2;
            hoverPillOptions.IsVisible = true;
            hoverPillOptions.IsEnabled = hoverPillCheckBox.IsChecked == true;
            hoverAnimationOptions.IsVisible = true;
            hoverOpenDurationInput.IsEnabled = SelectedSettings?.HoverOpenEffect != Types.HoverAnimationEffect.None;
            hoverCloseDurationInput.IsEnabled = SelectedSettings?.HoverCloseEffect != Types.HoverAnimationEffect.None;
            string area = selectedSegment switch { 1 => "Apps", 2 => "System tray", 3 => "Widgets", _ => "Entire taskbar" };
            hoverEditingLabel.Text = "Editing: " + area;
            bool hidden = selectedSegment == 2 && !activeSettings.ShowTray && !activeSettings.DynamicTrayLayout.ShowOnHover
                || selectedSegment == 3 && !activeSettings.ShowWidgets && !activeSettings.DynamicWidgetsLayout.ShowOnHover;
            selectedAreaHint.Text = hidden ? "This area is hidden. Change its visibility to see your adjustments."
                : "Shape and hover settings affect only " + area + ".";
            selectedAreaHint.IsVisible = hidden;
            hoverAvailabilityHint.IsVisible = !anyHover;
            hoverAvailabilityHint.Text = !anyHover
                ? "Enable Auto-hide for " + area + " to use these options."
                : selectedSegment is 1 or 2 && SelectedSettings.HoverActivationPill
                    ? "Move the pointer onto the bottom handle to reveal " + area + "."
                    : "Move the pointer over the " + area + " area to reveal it.";
        }

        private void HoverNumeric_ValueChanged(object sender, NumericUpDownValueChangedEventArgs e)
        {
            if (!initialized || loadingEditor || SelectedSettings == null || sender is not NumericUpDown input || input.Value is not decimal value) return;
            if (input == hoverPillWidthInput) SelectedSettings.HoverPillWidth = (int)value;
            else if (input == hoverPillThicknessInput) SelectedSettings.HoverPillThickness = (int)value;
            else if (input == hoverDelayInput) SelectedSettings.HoverHideDelaySeconds = (double)value;
            else if (input == hoverOpenDurationInput) SelectedSettings.HoverOpenDurationMs = (int)value;
            else if (input == hoverCloseDurationInput) SelectedSettings.HoverCloseDurationMs = (int)value;
            PreviewEditorChange();
        }

        private void HoverPill_Changed(object sender, RoutedEventArgs e)
        {
            if (!initialized || loadingEditor || SelectedSettings == null) return;
            SelectedSettings.HoverActivationPill = hoverPillCheckBox.IsChecked == true;
            RefreshHoverOptions();
            PreviewEditorChange();
        }

        private static Types.HoverAnimationEffect EffectFromIndex(int index) => index switch
        {
            1 => Types.HoverAnimationEffect.Slide,
            2 => Types.HoverAnimationEffect.Expand,
            _ => Types.HoverAnimationEffect.None
        };

        private static int EffectIndex(Types.HoverAnimationEffect effect) => effect switch
        {
            Types.HoverAnimationEffect.Slide => 1,
            Types.HoverAnimationEffect.Expand => 2,
            _ => 0
        };

        private void HoverEffect_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!initialized || loadingEditor || SelectedSettings == null) return;
            if (sender == hoverOpenEffectCombo && hoverOpenEffectCombo.SelectedIndex >= 0)
                SelectedSettings.HoverOpenEffect = EffectFromIndex(hoverOpenEffectCombo.SelectedIndex);
            if (sender == hoverCloseEffectCombo && hoverCloseEffectCombo.SelectedIndex >= 0)
                SelectedSettings.HoverCloseEffect = EffectFromIndex(hoverCloseEffectCombo.SelectedIndex);
            PreviewEditorChange();
        }

        private void PreviewEditorChange()
        {
            if (!initialized || loadingEditor || exiting) return;
            RefreshHoverOptions();
            SyncGeneralAutoHide();
            if (!activeSettings.IsWindowsDefault) AutoHide(true, taskbarDetails);
            foreach (var taskbar in taskbarDetails) taskbar.Ignored = true;
            saveEditorTimer.Stop();
            saveEditorTimer.Start();
        }

        private async void splitHelpButton_Click(object sender, RoutedEventArgs e)
        {
            Infobox ib = new Infobox();
            ib.Title = "RoundedTB - Split mode configuration";
            ib.titleBlock.Text = "How to use Split Mode";
            ib.bodyBlock.Text = "Split mode has a couple of limitations and requires a small amount of setup to get working properly.\n\nLimitations:\n1) Split mode doesn't resize itself automatically. This feature will be coming to RoundedTB for Windows 10 in the future.\n2) Toolbars are not compatible with split mode currently, and will need to be disabled apart from one (more on that in a moment).\n3) Split mode only works when the taskbar is horizontal at the top or bottom of the screen.\n\nSetup:\n1) Right-click the taskbar and disable \"Lock the taskbar\".\n2) Right-click it again and turn off any existing toolbars.\n3) Right-click a third time, select Toolbars > Desktop.\n4) Use the small || handle to resize the taskbar as you please.";
            await ib.ShowDialog(this);
        }

        private bool testingAppAttention;
        private bool attentionTestActive;
        private uint taskbarButtonCreatedMessage;
        private bool taskbarButtonReady;
        private readonly TaskbarTestIndicator attentionTestIndicator = new();
        internal bool IsAttentionTestWindow(IntPtr window) => attentionTestActive && window == NativeHandle;

        private IntPtr TaskbarButtonHook(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (taskbarButtonCreatedMessage != 0 && message == taskbarButtonCreatedMessage) taskbarButtonReady = true;
            return IntPtr.Zero;
        }

        private void StopAttentionTest()
        {
            if (!attentionTestActive) return;
            attentionTestActive = false;
            try
            {
                var stop = new LocalPInvoke.FLASHWINFO
                {
                    Size = (uint)Marshal.SizeOf<LocalPInvoke.FLASHWINFO>(), Window = NativeHandle, Flags = 0
                };
                LocalPInvoke.FlashWindowEx(ref stop);
                attentionTestIndicator.Clear();
            }
            catch (Exception error) { interaction?.AddLog("Could not clear notification test: " + error); }
            appAttentionReveal.Notify(4, NativeHandle, activeSettings);
        }

        private async void TestAppAttention_Click(object sender, RoutedEventArgs e)
        {
            if (!OperatingSystem.IsWindows() || exiting || applying || testingAppAttention
                || !shellHookRegistered || !AppAttentionReveal.Enabled(activeSettings)) return;
            testingAppAttention = true;
            RefreshSettingAvailability();
            var previousState = WindowState;
            int testStyle = appAttentionTestStyleComboBox.SelectedIndex;
            try
            {
                StopAttentionTest();
                if (testStyle != 0 && !taskbarButtonReady)
                    throw new InvalidOperationException("Windows has not created RoundedTB's taskbar button yet. Try again in a moment.");
                attentionTestActive = true;
                attentionTestIndicator.Show(NativeHandle, testStyle);
                // Minimizing naturally transfers focus; Windows can reject attempts
                // to give another process foreground focus programmatically.
                WindowState = WindowState.Minimized;
                var wait = Stopwatch.StartNew();
                while (!exiting && LocalPInvoke.GetForegroundWindow() == NativeHandle && wait.ElapsedMilliseconds < 1500)
                    await Task.Delay(25);
                if (exiting) return;
                if (LocalPInvoke.GetForegroundWindow() == NativeHandle)
                {
                    StopAttentionTest();
                    WindowState = previousState;
                    appAttentionHint.Text = "Test could not move focus away from RoundedTB. Try again after switching to another window.";
                    return;
                }
                var flash = new LocalPInvoke.FLASHWINFO
                {
                    Size = (uint)Marshal.SizeOf<LocalPInvoke.FLASHWINFO>(),
                    Window = NativeHandle, Flags = 2 | 12, Count = 0, Timeout = 0
                };
                // The return value is the previous caption state, not a success flag.
                LocalPInvoke.FlashWindowEx(ref flash);
            }
            catch (Exception error)
            {
                StopAttentionTest();
                if (!exiting) WindowState = previousState;
                appAttentionHint.Text = "Could not run the flash test: " + error.Message;
                interaction?.AddLog(error.ToString());
            }
            finally
            {
                testingAppAttention = false;
                if (!exiting) RefreshSettingAvailability();
            }
        }

        private async void TestWindowsApis_Click(object sender, RoutedEventArgs e)
        {
            if (!initialized || exiting || applying) return;
            applying = true;
            bool resume = taskbarThread.IsBusy;
            restoreWindowsButton.IsEnabled = settingsScrollViewer.IsEnabled = false;
            diagnosticsReport.IsVisible = true;
            diagnosticsReport.Text = "Testing Windows API access…";
            try
            {
                if (resume)
                {
                    taskbarThread.CancelAsync();
                    while (taskbarThread.IsBusy) await Task.Delay(25);
                }
                string report = await Task.Run(() => new WindowsApiDiagnostics().Run());
                diagnosticsReport.Text = report;
                if (OperatingSystem.IsWindows())
                {
                    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rtb-diagnostics.log");
                    try
                    {
                        await File.AppendAllTextAsync(path, report + Environment.NewLine);
                        diagnosticsReport.Text += Environment.NewLine + "Report saved to " + path;
                    }
                    catch (Exception error) { diagnosticsReport.Text += Environment.NewLine + "Could not save report: " + error.Message; }
                }
            }
            catch (Exception error)
            {
                diagnosticsReport.Text += Environment.NewLine + "Test stopped: " + error.Message;
                interaction?.AddLog(error.ToString());
            }
            finally
            {
                if (resume && !exiting && !activeSettings.IsWindowsDefault && !taskbarThread.IsBusy)
                    taskbarThread.RunWorkerAsync();
                applying = false;
                restoreWindowsButton.IsEnabled = settingsScrollViewer.IsEnabled = true;
                UpdateUi();
            }
        }

        private async void aboutButton_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow aw = new AboutWindow();
            await aw.ShowDialog(this);
        }

        private void fillMaximisedCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (isWindows11)
            {
                fillAltTabCheckBox.IsEnabled = true;
            }
        }

        private void fillMaximisedCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            fillAltTabCheckBox.IsEnabled = isWindows11;

        }

        private void showSegmentsOnHoverCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            SelectedSettings.ShowOnHover = true;
            showTrayCheckBox.IsEnabled = activeSettings.IsDynamic && !activeSettings.DynamicTrayLayout.ShowOnHover;
            showWidgetsCheckBox.IsEnabled = activeSettings.IsDynamic && !activeSettings.DynamicWidgetsLayout.ShowOnHover;
            foreach (var taskbar in taskbarDetails)
            {
                taskbar.AppListHovered = false;
                taskbar.TrayHovered = false;
                taskbar.WidgetsHovered = false;
            }
            PreviewEditorChange();
            UpdateUi();
        }

        private void showSegmentsOnHoverCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            SelectedSettings.ShowOnHover = false;
            if (selectedSegment == 2)
            {
                activeSettings.ShowTray = true;
                showTrayCheckBox.IsChecked = true;
            }
            if (selectedSegment == 3)
            {
                activeSettings.ShowWidgets = true;
                showWidgetsCheckBox.IsChecked = true;
            }
            showTrayCheckBox.IsEnabled = activeSettings.IsDynamic && !activeSettings.DynamicTrayLayout.ShowOnHover;
            showWidgetsCheckBox.IsEnabled = activeSettings.IsDynamic && !activeSettings.DynamicWidgetsLayout.ShowOnHover;
            PreviewEditorChange();
            UpdateUi();
        }

        internal void InitializeUi(Types.Settings settings, bool windows11)
        {
            initialized = false;
            activeSettings = settings;
            if (settings.ExpandMaximizedWorkArea) settings.FillOnMaximise = false;
            Types.SegmentSettings DefaultSegment() => new Types.SegmentSettings
            {
                CornerRadius = windows11 ? 7 : 16,
                MarginTop = windows11 ? 3 : 2,
                MarginLeft = windows11 ? 3 : 2,
                MarginBottom = windows11 ? 3 : 2,
                MarginRight = windows11 ? 3 : 2
            };
            settings.SimpleTaskbarLayout ??= DefaultSegment();
            settings.DynamicAppListLayout ??= DefaultSegment();
            settings.DynamicTrayLayout ??= DefaultSegment();
            settings.DynamicWidgetsLayout ??= DefaultSegment();
            SettingsSanitizer.Normalize(settings);
            if (settings.ShowSegmentsOnHover)
            {
                // The old global option controlled only the tray and widgets.
                settings.DynamicTrayLayout.ShowOnHover = true;
                settings.DynamicWidgetsLayout.ShowOnHover = true;
                settings.ShowSegmentsOnHover = false;
            }
            isWindows11 = windows11;
            activeSettings.IsWindows11 = windows11;
            dynamicCheckBox.Content = windows11 ? "Separate apps, widgets, and system tray" : "Separate apps and system tray";
            dynamicCheckBox.IsChecked = settings.IsDynamic;
            centredCheckBox.IsChecked = settings.IsCentred;
            showTrayCheckBox.IsChecked = settings.ShowTray;
            showWidgetsCheckBox.IsChecked = settings.ShowWidgets;
            fillMaximisedCheckBox.IsChecked = settings.FillOnMaximise;
            fillAltTabCheckBox.IsChecked = windows11 && settings.FillOnTaskSwitch;
            fillAltTabCheckBox.IsEnabled = windows11;

            showSegmentsOnHoverCheckBox.IsEnabled = true;
            showTrayCheckBox.IsEnabled = settings.IsDynamic && !settings.DynamicTrayLayout.ShowOnHover;
            showWidgetsCheckBox.IsEnabled = settings.IsDynamic && !settings.DynamicWidgetsLayout.ShowOnHover;
            SegmentHover.MigrateLegacyAutoHide(settings);
            revealOnAppOpenCheckBox.IsChecked = settings.RevealOnAppOpen;
            revealOnAppAttentionCheckBox.IsChecked = settings.RevealOnAppAttention;
            keepAppAttentionOpenCheckBox.IsChecked = settings.KeepAppAttentionOpenUntilAccepted;
            appAttentionDurationInput.Value = (decimal)AppAttentionReveal.DurationMilliseconds(settings) / 1000;
            appOpenDurationInput.Value = (decimal)AppOpenReveal.DurationMilliseconds(settings) / 1000;
            expandWorkAreaCheckBox.IsChecked = settings.ExpandMaximizedWorkArea;
            settings.SimpleTaskbarLayout.MaximizedVisibility ??= settings.SimpleTaskbarLayout.ShowOnHover ? Types.VisibilityMode.AutoHide : Types.VisibilityMode.AlwaysVisible;
            settings.DynamicAppListLayout.MaximizedVisibility ??= settings.DynamicAppListLayout.ShowOnHover ? Types.VisibilityMode.AutoHide : Types.VisibilityMode.AlwaysVisible;
            settings.DynamicTrayLayout.MaximizedVisibility ??= settings.DynamicTrayLayout.ShowOnHover ? Types.VisibilityMode.AutoHide : settings.ShowTray ? Types.VisibilityMode.AlwaysVisible : Types.VisibilityMode.Hidden;
            settings.DynamicWidgetsLayout.MaximizedVisibility ??= settings.DynamicWidgetsLayout.ShowOnHover ? Types.VisibilityMode.AutoHide : settings.ShowWidgets ? Types.VisibilityMode.AlwaysVisible : Types.VisibilityMode.Hidden;
            LoadEditor(settings.IsDynamic ? 1 : 0);
            UpdateUi();
            initialized = true;
            RefreshSettingAvailability();
        }

        private Types.SegmentSettings SelectedSettings => selectedSegment switch
        {
            1 => activeSettings.DynamicAppListLayout,
            2 => activeSettings.DynamicTrayLayout,
            3 => activeSettings.DynamicWidgetsLayout,
            _ => activeSettings.SimpleTaskbarLayout
        };

        private void CommitEditor()
        {
            if (SelectedSettings == null) return;
            mTopInput_LostFocus(null, null);
            mBottomInput_LostFocus(null, null);
            mLeftInput_LostFocus(null, null);
            mRightInput_LostFocus(null, null);
            cornerRadiusInput_LostFocus(null, null);
        }

        private void LoadEditor(int segment)
        {
            loadingEditor = true;
            try
            {
                selectedSegment = segment;
                notificationsSection.IsVisible = segment < 2;
                appVisibilityCombo.IsVisible = segment < 2;
                trayVisibilityCombo.IsVisible = segment == 2;
                widgetsVisibilityCombo.IsVisible = segment == 3;
                visibilityEditingLabel.Text = segment switch { 2 => "Tray", 3 => "Widgets", _ => "Taskbar" };
                RefreshVisibilityButtons();
                var settings = SelectedSettings;
                showSegmentsOnHoverCheckBox.IsChecked = settings.ShowOnHover;
                showSegmentsOnHoverCheckBox.IsEnabled = true;
                hoverPillCheckBox.IsChecked = settings.HoverActivationPill;
                hoverPillWidthInput.Value = Math.Clamp(settings.HoverPillWidth, 16, 600);
                hoverPillThicknessInput.Value = Math.Clamp(settings.HoverPillThickness, 1, 12);
                hoverDelayInput.Value = (decimal)Math.Clamp(settings.HoverHideDelaySeconds, 0, 60);
                hoverOpenDurationInput.Value = Math.Clamp(settings.HoverOpenDurationMs, 0, 5000);
                hoverCloseDurationInput.Value = Math.Clamp(settings.HoverCloseDurationMs, 0, 5000);
                hoverOpenEffectCombo.SelectedIndex = EffectIndex(settings.HoverOpenEffect);
                hoverCloseEffectCombo.SelectedIndex = EffectIndex(settings.HoverCloseEffect);
                RefreshHoverOptions();
                cornerRadiusInput.Text = settings.CornerRadius.ToString();
                mTopInput.Text = settings.MarginTop.ToString();
                mLeftInput.Text = settings.MarginLeft.ToString();
                mBottomInput.Text = settings.MarginBottom.ToString();
                mRightInput.Text = settings.MarginRight.ToString();
                taskbarRectStandIn.Classes.Set("selected", segment < 2);
                trayRectStandIn.Classes.Set("selected", segment == 2);
                widgetsRectStandIn.Classes.Set("selected", segment == 3);
                dynamicCheckBox.IsVisible = false;
                showTrayCheckBox.IsVisible = segment == 2;
                showWidgetsCheckBox.IsVisible = segment == 3;
            }
            finally { loadingEditor = false; }
        }

        private void taskbarRectStandIn_Click(object sender, RoutedEventArgs e)
        {
            CommitEditor();
            LoadEditor(activeSettings.IsDynamic ? 1 : 0);
        }

        private void trayRectStandIn_Click(object sender, RoutedEventArgs e)
        {
            CommitEditor();
            LoadEditor(2);
        }

        private void widgetsRectStandIn_Click(object sender, RoutedEventArgs e)
        {
            CommitEditor();
            LoadEditor(3);
        }

        private void mTopInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(mTopInput.Text, out int check) && mTopInput.Text != string.Empty)
            {
                switch (selectedSegment)
                {
                    default:
                        break;

                    case 0:
                        activeSettings.SimpleTaskbarLayout.MarginTop = check;
                        break;

                    case 1:
                        activeSettings.DynamicAppListLayout.MarginTop = check;
                        break;

                    case 2:
                        activeSettings.DynamicTrayLayout.MarginTop = check;
                        break;

                    case 3:
                        activeSettings.DynamicWidgetsLayout.MarginTop = check;
                        break;
                }
            }
        }

        private void mBottomInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(mBottomInput.Text, out int check) && mBottomInput.Text != string.Empty)
            {
                switch (selectedSegment)
                {
                    default:
                        break;

                    case 0:
                        activeSettings.SimpleTaskbarLayout.MarginBottom = check;
                        break;

                    case 1:
                        activeSettings.DynamicAppListLayout.MarginBottom = check;
                        break;

                    case 2:
                        activeSettings.DynamicTrayLayout.MarginBottom = check;
                        break;

                    case 3:
                        activeSettings.DynamicWidgetsLayout.MarginBottom = check;
                        break;
                }
            }
        }

        private void mLeftInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(mLeftInput.Text, out int check) && mLeftInput.Text != string.Empty)
            {
                switch (selectedSegment)
                {
                    default:
                        break;

                    case 0:
                        activeSettings.SimpleTaskbarLayout.MarginLeft = check;
                        break;

                    case 1:
                        activeSettings.DynamicAppListLayout.MarginLeft = check;
                        break;

                    case 2:
                        activeSettings.DynamicTrayLayout.MarginLeft = check;
                        break;

                    case 3:
                        activeSettings.DynamicWidgetsLayout.MarginLeft = check;
                        break;
                }
            }
        }

        private void mRightInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(mRightInput.Text, out int check) && mRightInput.Text != string.Empty)
            {
                switch (selectedSegment)
                {
                    default:
                        break;

                    case 0:
                        activeSettings.SimpleTaskbarLayout.MarginRight = check;
                        break;

                    case 1:
                        activeSettings.DynamicAppListLayout.MarginRight = check;
                        break;

                    case 2:
                        activeSettings.DynamicTrayLayout.MarginRight = check;
                        break;

                    case 3:
                        activeSettings.DynamicWidgetsLayout.MarginRight = check;
                        break;
                }
            }
        }

        private void cornerRadiusInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(cornerRadiusInput.Text, out int check) && cornerRadiusInput.Text != string.Empty)
            {
                switch (selectedSegment)
                {
                    default:
                        break;

                    case 0:
                        activeSettings.SimpleTaskbarLayout.CornerRadius = check;
                        break;

                    case 1:
                        activeSettings.DynamicAppListLayout.CornerRadius = check;
                        break;

                    case 2:
                        activeSettings.DynamicTrayLayout.CornerRadius = check;
                        break;

                    case 3:
                        activeSettings.DynamicWidgetsLayout.CornerRadius = check;
                        break;
                }

            }
        }

    }
}
