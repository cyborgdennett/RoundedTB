# Settings edge-case audit — 2026-10-12

The release regression suite passes 205 tests. This audit includes code inspection, Avalonia headless settings tests, visibility/animation combination tests, and interactive checks on the development Windows 11 desktop. It does not certify every Windows build or hardware configuration.

## Confirmed defects fixed

- Enabling an animation on an already-visible segment replayed its opening and briefly clipped it. Changes now start at the current progress.
- Increasing animation duration during a transition moved the segment backward. Effect/duration changes now rebase the transition without reversing progress.
- Switching an active attention reveal from “keep open until accepted” to a timed reveal retained an unlimited deadline. Active flashes now use the current preference and duration, measured from their first flash.
- Disabling app-open reveal left its timer pending, allowing re-enabling to reveal an old event. Disabling now clears pending reveals and reseeds existing windows.
- Changing the app-open duration did not affect an active reveal. Duration now applies from the opening event; completed events are cleared so increasing duration cannot replay them.
- A non-finite saved hover delay could throw during settings initialization. Saved delays, durations, activation rectangle dimensions, and enum values are normalized before loading controls.

Persistence now writes a flushed temporary file followed by an atomic replacement instead of truncating the settings document. Malformed JSON is backed up before defaults load. Extreme spacing/corner inputs at high DPI are bounded for native geometry; reversed or empty rectangles produce empty clipping regions.

## Automated coverage

- 324 combinations of normal/maximized visibility across Apps, Tray, and Widgets. Each checks ordinary visibility, Start, thumbnail preview, tray flyouts, attention, temporary restoration, and switching back without mutating saved choices.
- 432 combinations of show/hide effects, zero/short/long durations, and zero/long hide delays. Transitions finish, respect delays, and do not overshoot.
- Existing settings tests cover immediate changes, separate segment preferences, normal/maximized choices, incompatible experimental/maximized restoration options, None effect dependencies, numeric arrows/spinners, missing/legacy settings, narrow layouts, the persistent enable/restore control, and settings-close behavior.
- Existing behavior tests cover activation rectangles, preview/menu visibility, centered crowded taskbars, app-open/attention rules, fullscreen edge geometry, and work-area eligibility.
- New regressions cover live animation/timer changes, invalid numeric settings, extreme geometry, malformed JSON preservation, and repeated settings saves.

## Windows desktop results

- Settings navigation, immediate persistence, and enable/restore control: passed.
- API diagnostics: no access denied or other failures in tested operations. Three operations were explicitly not verified by the diagnostics tool.
- Normal/maximized visibility changes: passed.
- Calculator app-open reveal: approximately two seconds; shortening an active reveal to half a second measured 503 ms in the final installed build using `-LiveDuration`.
- Attention reveal: approximately two seconds; activation dismissed it and a notification after acceptance revealed it again.
- Chrome F11 + Windows key: tray visible, Chrome client fills the monitor, final screen row matches the browser background.
- Experimental auto-hide: 45 ms reveal in this run, no native slide/activation line, stable maximized geometry, and work-area restoration passed.
- Experimental always-visible: stationary opaque taskbar during the stability check, existing maximized Chrome expanded when enabled, and cleanup passed.
- PID kill: native taskbar regions/styles/work area restored. Settings and taskbar preview close now keep the app running; full exit is available from the tray.

The native audit wrapper temporarily widens settings so section links are available and restores the original size. Individual scripts restore their changed preferences and close their own test windows.

## Remaining release checks

Physical mixed-DPI multi-monitor arrangements, monitor hot-plugging, sleep/resume, Explorer restart, sign-out/reboot startup, and different Windows builds need additional real-device testing. Arithmetic coverage is not a substitute for observing shell layout at every scale. Startup was not retested by signing the user out during this audit.

Attention timeout intentionally does not restart for repeated flashes from an unaccepted window. The next notification is rearmed by activation, destruction, or disabling/re-enabling attention detection. The current tracker treats an app that stops and later flashes again without ever being activated as part of that unaccepted sequence. Distinguishing those sequences remains a limitation requiring further shell-event investigation.
