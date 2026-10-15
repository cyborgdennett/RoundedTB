# Maximized work-area gap during unlock

## Reproduction

Enable experimental **Use taskbar space while maximized**, maximize Chrome,
then lock and unlock Windows. A taskbar-height wallpaper gap can appear below
Chrome. Native taskbar auto-hide is disabled in the current implementation;
RoundedTB expands the work area with `SPI_SETWORKAREA` instead.

## Evidence from the affected machine

The baseline trace `session-20261014-192959-044-8.json` shows the primary
work-area bottom repeatedly returning from 1440 to 1392. Chrome's frame then
shrinks to 1392. Expansion restores both until another reset. This precedes
the visible gap and matches the 48-pixel native taskbar reservation.

Faster polling, additional foreign-window positioning, and work-area change
broadcasts did not fix the issue and increased pulsing. Those behavior changes
were rolled back. Current instrumentation records session geometry plus
RoundedTB's own work-area and appbar-state writes; it does not add repairs.

The real lock/unlock comparison `native-autohide-20261014-194710-680.json`
used Windows default mode and native auto-hide, without manual expansion or
Chrome positioning during capture. Across 3,306 samples, the work-area bottom
and Chrome's outer frame bottom remained 1440. Native appbar state remained 1.
The user reported no wallpaper flicker. Chrome client height was occasionally
1438 despite its full-height outer frame; this is separate from the 48-pixel
wallpaper gap. Original native state and RoundedTB preferences were restored.

These observations support a conflict with Windows' taskbar reservation.
They do not identify the process or call stack responsible for each baseline
work-area reset, nor establish behavior on other Windows builds or monitors.

## Architectural constraint

The shell maintains appbar reservations separately from a work-area override.
See Microsoft's [appbar documentation](https://learn.microsoft.com/en-us/windows/win32/shell/application-desktop-toolbars).
Native auto-hide releases the reservation in the tested configuration, but
also delegates reveal/retract timing to Explorer. No documented permanent
hold-open mechanism was found. Repeated positioning, focus stealing, or
synthetic notification/busy conditions are not validated substitutes.

Do not silently switch the feature to native auto-hide: instant segment reveal
and Always Visible are existing requirements. Resolve that behavior tradeoff
before changing the work-area architecture. Synthetic session messages alone
cannot validate the actual unlock transition.

## Session-only handoff experiment

The user rejected permanent native auto-hide and requested a temporary handoff.
The experimental build enables native auto-hide on the lock notification, pauses
manual work-area expansion while it is active, then restores the saved native
state after the first unlocked worker processing frame. It immediately repairs
the expanded work area once before returning to the baseline maintenance
interval. This is a processing frame, not proof of a frame presented by DWM.

Duplicate lock/unlock/desktop-ready notifications must not replace the saved
state or restore it twice. Disabling expansion, restoring Windows, and exiting
cancel the handoff before restoring work areas. The existing recovery process
restores its pre-handoff snapshot if the application is killed.

The real test failed: Chrome remained full-height with native auto-hide on,
then shrank after native auto-hide was restored to off. The shell subsequently
reinstalled the 48-pixel reservation despite the immediate repair. No sustained
native auto-hide mode or faster maintenance polling is introduced.

The failed session-only handoff was subsequently removed. The installed app
again uses the pre-handoff behavior plus passive diagnostics.

## Isolated native auto-hide OFF transition

The user requested investigation of a flicker-free OFF transition. Two signed-in
comparisons released the reservation with native auto-hide, then turned it off
and expanded the work area once. No foreign-window positioning was performed.
Native calls ran on a separate thread so geometry sampling continued during
blocking API calls.

* `native-autohide-off-20261014-201026-433.json`: OFF took 266 ms; silent
  expansion took 15 ms. Both the work area and Chrome frame subsequently
  returned to 1392 and stayed there until diagnostic cleanup.
* `native-autohide-off-20261014-201046-526.json`: OFF took 281 ms;
  `SPIF_SENDCHANGE` expansion took 1,610 ms. Chrome shrank first, then the work
  area returned to 1392 during the broadcast. Notification did not preserve
  the expanded area.

These tests reproduced the reservation conflict independently of unlock.
They do not prove that every possible OFF transition fails.

Both subsequent window-lock comparisons also failed. In
`native-autohide-off-20261014-201541-746.json`, `LockWindowUpdate` succeeded at
tick 178285250 and was released at 178286343, but a sample beginning at
178285546 already showed Chrome's frame at 1392. It did not prevent the resize.
The silent-expansion variant failed as well. Window locks remain diagnostic-only.

## Further architecture investigation

Windhawk's [taskbar auto-hide mod](https://github.com/ramensoftware/windhawk-mods/blob/main/mods/taskbar-auto-hide-when-maximized.wh.cpp)
has a Never auto-hide mode. Its source keeps native auto-hide enabled while
hooking Explorer's private hide functions. This potentially preserves released
space and a visible taskbar, but requires in-process hooks and version-specific
symbol resolution. This investigation led to the native helper described below; no Windhawk installation is required.

That source also identifies the primary taskbar's hide timer as ID 2. The
external diagnostic made 37 cancellation attempts; all returned false with
last error 0. Both observation and cancellation runs retained a full-sized
native taskbar HWND. This is inconclusive: no active timer was established,
and modern Windows 11 also controls expansion in Taskbar.View.dll's XAML view
coordinator. HWND bounds alone do not prove icon visibility. No timer control
is added to product behavior.

## Startup comparison

Startup waits for Explorer's taskbar to exist, starts the worker, and applies
the same native-off/manual-expansion work-area mode. Restart registration and
the sign-in task control process launch; neither provides an unlock presentation
barrier. Newly restored apps might maximize after expansion, unlike existing
windows during unlock, but that ordering has not been established by a matched
startup trace. Re-running the complete setup would first restore the original
work area and could introduce another shrink. The baseline worker already
reapplies the desired expanded area without that extra reset.

## Comparison script

Run `RoundedTB.Tests/PowerShell/Measure-NativeAutoHideUnlock.ps1 -Seconds 120
-TemporarilyRestoreRoundedTB` in the interactive Windows session. It temporarily
restores the native taskbar, records geometry while the user manually locks and
unlocks, and re-enables RoundedTB afterward. The automatic mode requires the
separate-segment layout. `-CompileOnly` validates native bindings without
changing desktop state. Logs remain local under
`%LOCALAPPDATA%\RoundedTB\Diagnostics`.

## Integrated solution

The successful prototype is integrated behind the existing experimental work-area checkbox. Native auto-hide stays enabled internally to avoid reinstating the reservation during unlock; private shell/view hooks prevent native retraction. RoundedTB continues to own segment visibility. Repeated manual lock/unlock tests showed no wallpaper flicker, with actual app WTS traces recording native state 1 and full-height work areas and maximized Chrome frames.

The packaged helper requires exact module identities and executable symbol addresses. Unsupported Windows builds fall back to the previous manual expansion path. Native controller death restores state independently of the managed recovery process. The main app reacquires the helper on the UI thread after primary taskbar replacement. See [native helper details](../native/TaskbarPin/README.md) for lifetime and version limitations.

PowerShell SystemEvents did not reliably detect unlock during standalone recording; those runs alone are not unlock evidence. The actual app uses WTS notifications.

## Integrated regression checks (1.0.1)

- Release headless suite: 225 passed, zero failures or skips.
- Experimental hover: 11 ms reveal latency, zero maximized size transitions,
  no visible activation-line frames, Chrome frame/work area both 1440 pixels.
- Always Visible: stable taskbar position with cursor away, zero maximized size
  transitions, existing maximized Chrome expands when enabling the feature.
- Experimental and normal mode: three F11 toggles, a window over fullscreen,
  repeated Alt+Tab, tray visibility, and final-row browser pixel checks passed.
- PID kill initially exposed a cleanup race. Managed recovery now waits for
  native hook shutdown before restoring Windows state and clearing clipping
  last. Two consecutive reruns passed, including native auto-hide off, original
  reservation/regions/styles, and settings/taskbar preview close semantics.
- All 27 PowerShell scripts parsed and current settings control IDs matched.

These local checks cover this single-monitor Windows installation, not other
Windows module versions, mixed DPI, or Explorer restart under every condition.
