# Native symbol compatibility checks

The app resolves unknown Windows modules from Microsoft's public symbol server. Matching symbols establish function identity and executable addresses; they do not certify taskbar behavior on every Windows version.

## Locally verified

- 246 headless tests pass, with no skips or failures.
- Real Microsoft PDB downloads resolve the same hook addresses as the Windows SDK resolver on this PC.
- An installed-app test without bundled descriptors resolves and attaches in the background in roughly 17 seconds; settings remain responsive.
- Only two small JSON address records remain (less than 1 KB combined); the PDB downloads are removed.
- Re-enabling the experimental feature uses the verified cache without another download.
- Offline clients can use cached records; an updated DLL cannot reuse old addresses.
- Corrupted cache structure, addresses outside executable sections, foreign PDB identities and changed signatures are rejected and repaired when matching symbols are available.
- Missing or malformed downloads leave no completed cache entry.
- Explorer restart initially revealed two bugs: native attachment attempted before Taskbar.View.dll loaded, and a missing TrayNotifyWnd handle remained cached. The updated build retries only the temporary native-module-not-loaded error and reacquires delayed tray child handles.
- The installed restart test passes using cached symbols only: RoundedTB survives, hooks reattach, and the Always Visible clock/notification area is included in the taskbar region.
- Forced-exit recovery passes after native retry/reattachment; Windows regions, styles and work area restore correctly.
- Experimental Chrome hover test passes after the tray fix: 26 ms reveal, zero activation-line frames or maximized size transitions, and the original work area restores when disabled.

## Remaining device coverage

Actual behavior on other Windows module versions, multiple monitors and mixed DPI still requires those systems. Synthetic PDB/cache tests and symbol identity checks are not substitutes for that coverage.

## Reproduction

PowerShell scripts are in `PowerShell/`:

- `Test-AutomaticTaskbarSymbols.ps1`: cold resolution; requires an empty automatic symbol cache and active rounded segments. Temporarily moves bundled descriptors aside, then restores them and preferences.
- `Test-ExplorerRestart.ps1 -UseCachedSymbols`: restarts the primary Explorer process and verifies native and tray recovery using the local cache. Folder windows in that Explorer process can close.
- `Test-TaskbarExitRecovery.ps1`: force-stop recovery and settings/taskbar preview close semantics.

Unit fixtures generate native MSF/PDB streams, including split blocks, stripped public PDB ages, changed signatures and OMAP rejection. Tests use fake HTTP handlers for offline and corrupted-download cases, so the headless suite has no symbol-server dependency.
