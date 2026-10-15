# Windows desktop tests

Run these scripts in an interactive Windows 11 session against the installed app at `%LOCALAPPDATA%\Programs\RoundedTB`. They use UI Automation and Win32 APIs and are separate from `dotnet test`. Run desktop tests sequentially: they move focus, change settings temporarily, and create test windows.

## Recommended commands

Run from the repository root:

```powershell
# Syntax and settings-control references; no desktop changes.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\RoundedTB.Tests\PowerShell\Test-ScriptInventory.ps1

# Settings, API diagnostics, maximized visibility, app-open timing, and attention.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\RoundedTB.Tests\PowerShell\Test-SettingsAudit.ps1 -LiveDuration

# Advanced attention button, persistent reveal, and error flash style.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\RoundedTB.Tests\PowerShell\Test-SettingsAudit.ps1 -AttentionButton -KeepOpen -TestStyle 2

# Experimental space reclamation, hover reveal, and always-visible stability.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\RoundedTB.Tests\PowerShell\Test-SettingsAudit.ps1 -Experimental
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\RoundedTB.Tests\PowerShell\Test-SettingsAudit.ps1 -Experimental -AlwaysShow

# Repeated F11 and Alt+Tab, including the full-width restoration regression.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\RoundedTB.Tests\PowerShell\Test-FullscreenStartMenu.ps1 -Chrome -F11Count 3 -WindowOverlay -AltTabPreview -RepeatSelection -NativeFullBar

# Win+Tab uses the same maintained fullscreen check.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\RoundedTB.Tests\PowerShell\Test-FullscreenStartMenu.ps1 -Chrome -WindowOverlay -TaskViewPreview -RepeatSelection
```

The audit wrapper widens settings while testing and restores its original size. Geometry tests require RoundedTB enabled with centered separate segments on the primary taskbar. Hover timing checks temporarily configure normal and maximized auto-hide, no activation rectangles or effects, and zero hide delay, then restore those preferences. They also temporarily disable maximized-window restoration while measuring reveal timing. The experimental hover check uses monitor-relative coordinates; these tests do not certify arbitrary taskbar arrangements or multiple monitors. Other maximized/fullscreen windows can affect a test's visibility expectations.

Chrome defaults to `C:\Program Files (x86)\Google\Chrome\Application\chrome.exe`. The two Chrome scripts accept `-ChromePath` for another installation. Task View checks browser geometry and taskbar visibility; its own desktop background is excluded from browser pixel comparisons. The fullscreen script asserts results by default, accepts odd `-F11Count` values, and saves a screenshot only when `-ScreenshotPath` is supplied. Use `-Chrome -WindowOverlay` for the ordinary window-over-fullscreen regression; omit overlay options to check Start and tray reveal. `-NativeFullBar` temporarily disables experimental mode and restores it afterward.

## Maintained scripts

| Script | Checks |
| --- | --- |
| `SettingsTestHelpers.ps1` | Shared setup and restoration of visibility and hover profiles; dot-sourced by timing tests. |
| `Test-ScriptInventory.ps1` | Parses every PowerShell script and checks literal control IDs against the current settings page. |
| `Test-SettingsAudit.ps1` | Runs the settings checks, `-AttentionButton`, or experimental checks; `-LiveDuration` shortens an active app-open reveal. |
| `Test-SettingsNavigation.ps1` | Continuous scrolling, section links, immediate saving, startup label, and persistent enable/restore control. |
| `Test-WindowsApiDiagnostics.ps1` | Advanced API diagnostics finish, controls resume, and saved preferences remain unchanged. |
| `Test-WindowModeVisibility.ps1` | Independent normal/maximized visibility using a real maximized window. |
| `Test-SegmentAutoHide.ps1` | Taskbar visibility, hover-control availability, and Calculator app-open reveal timing. |
| `Test-AppAttentionReveal.ps1` | Flash reveal timing and activation dismissal. `-UseRoundedTBButton` checks the Advanced test button; only that mode accepts `-KeepOpen` and `-TestStyle 0/1/2`. |
| `Test-ExperimentalInstantReveal.ps1` | Maximized space reclamation, hover latency or `-AlwaysShow` stability, fullscreen marking cleanup, and work-area restoration. |
| `Test-FullscreenStartMenu.ps1` | Fullscreen hiding, Start/tray reveal, foreground overlays, repeated F11, Alt+Tab/Win+Tab, and bottom-edge pixels. |
| `Test-MaximizedDragRestore.ps1` | Taskbar stays restored while dragging one of two maximized windows. |
| `Test-TaskbarExitRecovery.ps1` | PID kill restores native taskbar/work area; settings and taskbar preview close keep RoundedTB running. Restarts the app afterward. |
| `Measure-NativeAutoHideUnlock.ps1` | Manual unlock comparison: requires Restore Taskbar mode, temporarily enables Windows auto-hide for 120 seconds, records work areas and maximized-window geometry, then restores native state and work areas. `-TemporarilyRestoreRoundedTB` switches an existing separate-segment configuration to Windows default mode and re-enables it afterward. `-CompileOnly` checks bindings without desktop changes. Does not lock the PC or resize windows. |
| `Test-NativeAutoHideOffTransition.ps1` | Five-second signed-in comparison: temporarily restores Windows mode, settles native auto-hide ON, then turns it OFF and expands work areas once. `-ExpansionFlags 0` silently expands; `2` broadcasts the work-area change. A native worker permits geometry sampling during either transition. `-LockMaximizedWindow` requires exactly one maximized Chrome window and briefly uses `LockWindowUpdate` around the native calls, releasing it immediately afterward. Records native-call ticks without resizing Chrome, then restores original native state and RoundedTB mode. `-CompileOnly` performs no desktop changes. |
| `Test-NativeAutoHideTimer.ps1` | Five-second comparison of cancelling only primary taskbar hide timer ID 2 every 100 ms. Opens the native bar by hovering its edge, moves the cursor away, records cancellation results/errors and native geometry, then restores cursor, native state, and RoundedTB mode. `-ObserveOnly` records normal retraction without cancelling timers; `-CompileOnly` performs no desktop changes. |
| `Read-SessionWindowState.ps1` | Reads the latest unlock trace and checks the recorded maximized windows’ current style and RoundedTB fullscreen-classification properties. No desktop changes. |
| `Build-TaskbarPinPrototype.ps1` | Builds the shipping x64 native hook DLL using Visual Studio C++ Build Tools. Does not attach to Explorer. |
| `Resolve-TaskbarPinSymbols.ps1` | Resolves exact-build Microsoft symbols using Windows SDK Debugging Tools; caches symbols locally and optionally writes a descriptor with `-OutputPath`. No Explorer changes. |
| `Test-TaskbarPinPrototype.ps1` | Temporarily attaches the native prototype while RoundedTB remains enabled with experimental space reclamation. Records work areas, Chrome bounds, hook counters, and Start visibility, then restores native state. `-CaptureTaskbar` saves local taskbar-only screenshots. `-WaitForUnlock` relies on PowerShell SystemEvents, which was unreliable in testing; validate unlock using the installed app’s WTS traces and manual observation instead. Disable the app’s experimental mode before attaching a standalone controller. `-CompileOnly` performs no desktop changes. Requires the compiled DLL and exact symbol descriptor in repository artifacts. |
| `Test-TaskbarPinRecovery.ps1` | Starts a standalone prototype controller, terminates that controller while native auto-hide is on, and verifies watchdog restoration and hook deactivation. Does not terminate RoundedTB or Explorer. |
| `Preview-SettingsLayout.ps1` | Captures each section of the continuous settings page into repository `artifacts/settings-preview`, or `-OutputDirectory`. |

Run individual settings checks with the configuration window open and wide enough to show section links. Exit recovery requires Windows native auto-hide off and deliberately terminates RoundedTB. Tests restore their changed preferences and close their own test windows; desktop automation blocked by Windows is explicitly reported as unverified.

The old `Investigation` probes were removed: they targeted obsolete controls, duplicate scenarios, or abandoned native auto-hide experiments. The former experimental/maximized-restoration combination is also removed because those settings are mutually exclusive. See [the settings audit](../SettingsAudit.md) for the earlier audit's results and remaining device coverage.

`Test-Installer.ps1` checks the compiled setup file by installing twice into a temporary directory, verifying startup and Apps registration, and uninstalling. Run it on a clean test user or before installing RoundedTB through Setup. It restores the previous startup task and Start menu shortcut; it does not launch the temporary application.
