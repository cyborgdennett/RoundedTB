# Native taskbar pin

The x64 helper ships with RoundedTB and is used only by experimental **Use taskbar space while maximized**. Native auto-hide releases Windows’ taskbar reservation; the helper prevents native retraction, while RoundedTB’s regions control segment visibility. This avoids the work-area changes that briefly exposed wallpaper after unlock. No Windhawk installation is required.

The app validates the exact timestamp and image size of both Windows modules against packaged descriptors in `symbols/`. Unsupported builds retain the previous manual work-area implementation. The bundled descriptor covers the tested Windows 26220 module versions; a Windows update can require a new descriptor. The helper validates executable RVAs again inside Explorer before enabling hooks. It never guesses addresses.

The controller uses a content-addressed local DLL copy and a thread-specific `WH_CALLWNDPROC` hook for initialization. The registered message `RoundedTB.TaskbarPin.Start.1` passes the controller PID. A 352-byte shared mapping, defined in [sharedProtocol.h](sharedProtocol.h), carries configuration and status. `TrayUI::_Hide` and optional `CSecondaryTray::_AutoHide` become no-ops; `ViewCoordinator::ShouldTaskbarBeExpanded` returns true. Native auto-hide is enabled only after the helper reports ready.

Disabling the feature, exiting, or controller death restores the saved native state and disables patches. Disabled trampolines, mappings, and module references remain allocated until Explorer exits so in-flight calls cannot execute freed code. Repeated activation reuses hooks. This is an intentional lifetime policy; full unloading is not implemented.

MinHook 1.3.4 is vendored from `https://github.com/TsudaKageyu/minhook/archive/refs/tags/v1.3.4.tar.gz`, with its license in `vendor/minhook/LICENSE.txt`. Function signatures and the shell/view behavior were studied from the Windhawk taskbar auto-hide mod; no pointer-scanning implementation is copied.

## Development and validation

1. `RoundedTB.Tests/PowerShell/Build-TaskbarPinPrototype.ps1` builds the shipping DLL with Visual Studio C++ Build Tools. CI runs this before publishing.
2. `Resolve-TaskbarPinSymbols.ps1 -OutputPath <repo>\artifacts\native\symbols.json` uses Windows SDK Debugging Tools and Microsoft symbols. Review the exact identities and signatures before adding a packaged descriptor. Users do not need the SDK or symbol downloads.
3. Test the installed app with `Test-ExperimentalInstantReveal.ps1`, including `-AlwaysShow`, and `Test-TaskbarExitRecovery.ps1`.
4. Lock and unlock manually with Chrome maximized. The app records WTS session events and geometry under `%LOCALAPPDATA%\RoundedTB\Diagnostics`. Check the visible result as well as geometry.

Standalone prototype scripts remain diagnostic tools. Disable the app’s experimental feature before running a standalone controller to avoid competing controllers. PowerShell `SystemEvents.SessionSwitch` did not reliably report unlock in testing; `-WaitForUnlock` is not sufficient proof. Use the installed app’s WTS traces and manual observation.

The prototype passed repeated user-observed lock/unlock tests with no wallpaper flicker. Controller-death recovery restored native state and disabled hooks in 518 ms. These results do not certify other Windows versions or monitor arrangements.
