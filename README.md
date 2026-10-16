![RoundedTB](https://cdn.discordapp.com/attachments/272509873479221249/891555515799318568/unknown.png)

# RoundedTB
#### Add margins, rounded corners and segments to your taskbars!

![image](https://user-images.githubusercontent.com/31840547/134795141-76349eaf-12da-40f8-b2a0-d7b7c268d152.png)


## How do I get it?
The easiest way to download RoundedTB is from the [Microsoft Store](https://www.microsoft.com/store/productId/9MTFTXSJ9M7F). You can also download the latest version from the Releases tab, unzip it and run `RoundedTB.exe`. If you're a madman, you can compile it yourself or check out the latest [Canary build](https://nightly.link/torchgm/RoundedTB/workflows/ci/master/rtb-artifacts.zip) (note these can be very unfinished, buggy and unstable).

## Building this branch

This branch uses C#, Avalonia 12.1.4 and .NET 10. The application still requires Windows 10 (build 19041+) or Windows 11: the taskbar engine uses Win32 APIs to modify Explorer. The Avalonia views and headless UI tests can be built on Linux or macOS with the .NET 10 SDK.

```sh
dotnet build RoundedTB.sln -c Release
dotnet test RoundedTB.Tests/RoundedTB.Tests.csproj -c Release --no-build
```

Run on Windows with `dotnet run --project RoundedTB/RoundedTB.csproj`. Before publishing, build the native helper from x64 PowerShell on Windows with Visual Studio C++ Build Tools installed:

```powershell
.\RoundedTB.Tests\PowerShell\Build-TaskbarPinPrototype.ps1
```

Then create a standalone Windows x64 build, including the .NET runtime:

```sh
dotnet publish RoundedTB/RoundedTB.csproj -c Release -r win-x64 --self-contained true -o artifacts/RoundedTB
```

Install the published build from PowerShell with `./scripts/install.ps1 -EnableStartup`. The default destination is `%LOCALAPPDATA%\Programs\RoundedTB`, which works for every Windows username without administrator rights. The installer adds a Start Menu shortcut and updates sign-in startup to the installed executable. It preserves settings and leaves any old `%USERPROFILE%\RoundedTB` installation as a backup. Use `-NoLaunch` to install without starting the app.

Run `artifacts/RoundedTB/RoundedTB.exe`. Closing the settings window keeps RoundedTB in the system tray; choose **Close RoundedTB** in the tray menu to exit and restore the taskbar. A per-user Task Scheduler logon task with no configured delay uses `--startup` to launch with the settings window hidden. Existing `rtb.json` settings remain compatible, including the roaming location for packaged installations. The standalone CI builds the portable application and installer.

Before releasing, test on Windows: apply margins and corner radii to each segment, switch dynamic/split mode, toggle the tray with Win+F2, hide and reopen settings, launch a second instance, toggle run at startup, and exit to verify restoration. Also check Explorer restart, multiple monitors and DPI scaling, maximized-window/task-switch filling. Headless tests cover view resources, measured segment layout, initialized editor changes, mode switching, and missing settings; they do not exercise Explorer or the Windows tray. Startup and taskbar worker errors are written to `rtb.log` in the configuration directory (normally `%LOCALAPPDATA%`); the log rotates at 2 MiB.

## To use
### Basic options
The simplest way to use RoundedTB is by simply entering a margin and corner radius.
 - **Margin** - controls how many pixels to remove from each side of the taskbar, creating a margin around it that you can see and click through.
 -  **Corner Radius** - adjusts how round the corners of the taskbar should be.

### Advanced options
The advanced options allow for further customisation, at the cost of some user-friendliness.
- **Independent Margins** - in the advanced settings, a <kbd>...</kbd> button appears on the margin box. Click it to enable independent margins, which allow you to specify the margin for each side of the taskbar. You can also use negative values to hide the rounded corners for some sides, allowing you to "attach" the taskbar to different sides of the monitor.
- **Dynamic Mode (Windows 11)** - dynamic mode automatically resizes the taskbars to accommodate the number of icons in it, making the taskbar behave similarly to macOS' Dock.
- **Split Mode (Windows 10)** - split mode is a simplified version of dynamic mode for Windows 10. Due to a more limited taskbar, dynamically resizing the taskbar isn't possible. However after some setup, split mode allows you to separate the taskbar from the system tray and resize it at will. I admit it's certainly not as cool as dynamic mode but for now it's better than nothing 🥺. For info on setting up, see the bottom of this readme.
- **Show System Tray** - this toggles whether or not the system tray, clock etc. is displayed in dynamic/split mode. It can be toggled at any time by pressing <kbd>Win</kbd>+<kbd>F2</kbd>.
- **About RoundedTB** - provides information about the current version of RoundedTB. The "Debug" section lets you open the config and log files.







## Known issues
 - Auto-hiding is still incredibly experimental and may lead to a lot of flickering, especially with dynamic/split mode enabled. ([#36](https://github.com/torchgm/RoundedTB/issues/36))
 - Rounded corners are not antialiased due to a Windows limitation. ([#4](https://github.com/torchgm/RoundedTB/issues/4))
 - Dynamic mode won't hide the left side of the taskbar if the taskbar alignment has never been changed. This can be worked around by changing the alignment to Left and back to Center. ([#98](https://github.com/torchgm/RoundedTB/issues/98)) 
 - Dynamic mode/split mode only work correctly when the taskbar is horizontal at the top/bottom of the screen.
 - Split mode on Windows 10 only supports the main taskbar, secondary taskbars will not be split.
 - When using dynamic mode, the taskbar may occasionally become too large, too small or not update. This can usually be fixed by moving a window to or from that monitor or briefly changing the taskbar alignment. These issues will be reduced in upcoming updates, don't worry! I just need to refactor a lot of code first.
 - Compatibility with other taskbar mods is not guaranteed.

## Other info
RoundedTB is just a hobby of mine, and I'm certainly not an expert in this field, so I'm really sorry if you encounter a bug! If anything breaks catastrophically, press <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>Esc</kbd> to open Task Manager, end RoundedTB and then restart Explorer. At worst, just reboot your PC. RoundedTB makes no permanent changes (though it will run on startup if you enable it from the tray icon), so restarting should clear any issues.

Feel free to let me know about any bugs by filing an issue so I can look into it. Alternatively if you want to discuss RoundedTB, get some insider sneak-peeks, need some assistance or just want to see what I'm up to, then feel free to join the [Discord server](https://discord.gg/wYQJd8VGSB).

### Configuring split mode on Windows 10
Split mode has a couple of limitations and requires a small amount of setup to get working properly.
#### Limitations
- Split mode doesn't resize itself automatically. This feature will be coming to RoundedTB for Windows 10 in the future.
- Toolbars are not compatible with split mode currently, and will need to be disabled apart from one. This is because toolbars are used to mark the "empty" space on the taskbar.
- Split mode only works when the taskbar is horizontal at the top or bottom of the screen, and on the primary monitor.
#### Setup
1. Right-click the taskbar and disable "Lock the taskbar".
2. Right-click it again and turn off any existing toolbars.
3. Right-click a third time, select Toolbars > Desktop.
4. Use the small <kbd>||</kbd> handle to resize the taskbar as you please.

Watch the following video for a guide on setting up split mode:

https://user-images.githubusercontent.com/31840547/134795022-1312d011-40f2-4641-8c8d-3d6c0e752747.mp4


Startup timing diagnostics are saved to `%LOCALAPPDATA%\RoundedTB\startup.jsonl` (rotated at 1 MiB). Each launch records whether `--startup` was used, Windows session sign-in time, elapsed time before launch, Windows uptime, and initialization milestones. Sign-in time is queried from Windows session information; if unavailable, the record includes the error and leaves sign-in durations empty. These are session durations, not network connectivity or active usage time.

Standalone builds also register the running app for Windows session restoration when run at startup is enabled, using `--startup` to stay hidden. The logon task remains available when the app was closed before sign-out. The settings UI uses software rendering to avoid GPU initialization during sign-in; this does not change native taskbar rendering.

Release builds (`-c Release`) use the normal RoundedTB executable, window and tray icon and About banner. Debug builds retain the Canary icon and branding.

To configure hover separately, select Taskbar, Tray, or Widgets in the Appearance area picker and toggle **Auto-hide this area**. Each segment keeps its own preference and changes preview immediately in dynamic mode. Old global hover settings migrate to tray and widgets; the taskbar remains visible unless its own hover option is enabled.

Each hover segment also has a **Keep open after leaving** delay (0 seconds by default). Enable **Animate opening and closing** to choose a vertical reveal or expansion from the center, with separate opening and closing durations. These settings preview immediately and save per segment. Open Start menus and thumbnail previews keep the taskbar visible; the delay begins when that interaction ends.

**Experimental: let maximized windows use the taskbar space** reclaims the full taskbar area for maximized apps without reserving a bottom border. Check the experimental option. It works with the independent **Auto-hide Taskbar**, **Auto-hide Widgets**, and **Auto-hide Tray** choices in Layout & visibility. Auto-hide alone does not reclaim space. While active, restoration on maximization, snap preview, or task switching reveals the configured segments without replacing their rounded layout with a full-width Windows taskbar. Disabling the experimental option, restoring the Windows taskbar, or exiting from the tray restores the original work area and maximized window sizes. Closing settings keeps RoundedTB running. The option is off by default and marked experimental and unstable. It keeps Windows native auto-hide disabled and expands each monitor’s work area using `SPI_SETWORKAREA`, preserving a steady taskbar. Auto-hidden segments use the real pointer position to reveal immediately, without a visible bottom activation line. It temporarily marks ordinary maximized windows with Windows' documented `NonRudeHWND` property to keep native taskbar activation working. RoundedTB removes its own markings when windows restore, enter borderless fullscreen, or the feature stops.

For Taskbar and Tray, **Open from a bottom-edge pill** replaces the hidden segment with a thin activation surface at the bottom of the screen. Its width and thickness are adjustable per segment in logical pixels. Touch the pill to open, then move onto the icons to keep it open. Existing delays, animations, Start menu and thumbnail-preview behavior still apply.

### Configuration

Settings use one continuous scroll through **Layout & visibility**, **Shape & hover**, **App activity**, **Windows integration**, and **Advanced**. The links at the left jump within the same page.

Layout & visibility controls the rounded areas. Each area has one visibility choice: **Always visible** or **Auto-hide**; Widgets and the system tray also offer **Hidden**. Auto-hide keeps an area accessible by hovering; Hidden removes its background and icons from the rounded layout. Restoring the full Windows taskbar can temporarily show hidden areas. Widgets require Windows to provide a widgets button.

Shape & hover edits the selected area's margins, corners, reveal target, hide delay, and show/hide animations. Bottom handles are available for separate Apps and system tray areas. App activity reveals auto-hidden Apps when a window opens or a taskbar icon flashes; one continuous layout reveals the entire bar. Windows integration contains restoration during maximization/snap previews and task switching and **Launch RoundedTB at Windows sign-in**. Advanced contains Windows API diagnostics and the unstable screen-space experiment.

All changes take effect immediately and save automatically. The fixed top-right button shows **Restore Taskbar** while RoundedTB is active and **Enable RoundedTB** while the Windows taskbar is active. Switching to Windows retains your preferences; enabling resumes the rounded areas. With the experiment enabled, restoration reveals enabled rounded areas while retaining their shape; Hidden areas remain hidden.

Advanced also includes **Test Windows API access**. It pauses the taskbar worker, tests Explorer/window access and reversible native writes, then resumes the worker. The selectable report distinguishes access denied, other failures, and unverified operations; a copy is saved to `%LOCALAPPDATA%/rtb-diagnostics.log`. Same-value writes test API acceptance without toggling native auto-hide or resizing applications. Tests do not request elevation, change saved settings, or establish access to every future/elevated window.

Taskbar auto-hide includes **Show when an app flashes**, available with Auto-hide Taskbar. Enable it. Its reveal duration is configurable from 0.1–60 seconds (default: 5 seconds); the timer starts at the first flash and repeated flashes do not extend it, and activating or closing the app dismisses its request. Only the taskbar segment on the app’s monitor is revealed. Detection uses `RegisterShellHookWindow` and `HSHELL_FLASH` rather than ordinary redraw activity; it covers flashing taskbar icons, not all toast notifications.

The **Test attention** button in Advanced is available after enabling that option. It minimizes settings to release focus and flashes RoundedTB’s own taskbar icon continuously until you return, exercising the real notification path. The test style selector offers Normal, Warning (yellow paused-progress indicator), and Error (red error-progress indicator). Returning clears the flashing and colored indicator. Tests and actual app notifications follow the configured reveal duration, or **Keep visible until the app is activated or closed**. This option disables the duration field and keeps the segment visible until the notifying app is activated or closed.

RoundedTB starts an independent `RoundedTB.Recovery.exe` helper before changing the taskbar. It waits without polling for RoundedTB to exit, then restores the native taskbar and any expanded work area, including after a forced PID kill or `taskkill /IM RoundedTB.exe /F`. Killing the helper as well (for example, terminating the entire process tree) bypasses this recovery. Closing the settings window through its caption X, taskbar preview, or Alt+F4 hides settings; choose Close RoundedTB from the tray to exit and restore Windows.

## Standalone Windows installer

After publishing the Release `win-x64` build, install [Inno Setup 6](https://jrsoftware.org/isdl.php) on the build machine and run:

```powershell
.\scripts\build-installer.ps1 -Version 1.0.2
```

Send `artifacts\installer\RoundedTB-Setup-1.0.2-win-x64.exe` to users. It includes .NET, uses the normal RoundedTB icon, and defaults to `%LOCALAPPDATA%\Programs\RoundedTB` and lets users choose another writable installation folder. It installs without administrator permissions. The installer targets Windows 11 x64-compatible systems. Sign-in startup is optional; existing startup registration is preserved during upgrades. Uninstall through Windows Settings → Apps restores the taskbar and removes the app and its startup registration. Preferences and logs are retained.

CI builds and tests the installer and uploads it as `RoundedTB-installer`, alongside the portable application artifact. `Test-Installer.ps1` checks installation, upgrade, startup registration, and removal in a temporary directory; it refuses to run when this installer is already registered for the current user. The installer is currently unsigned, so Windows may display an unknown-publisher or SmartScreen prompt.

The experimental work-area mode includes an x64 native taskbar helper to prevent unlock flicker on exactly matched Windows module versions. Unknown module versions resolve matching Microsoft symbols in the background and cache verified addresses locally. Missing or incompatible symbols retain the manual work-area fallback. See [native helper details](native/TaskbarPin/README.md) for coverage and limitations.
