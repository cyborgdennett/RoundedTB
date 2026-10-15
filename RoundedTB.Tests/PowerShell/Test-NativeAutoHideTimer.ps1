param([switch]$CompileOnly,[switch]$ObserveOnly)
$ErrorActionPreference='Stop'
# ID 2 is the native primary-taskbar hide timer identified by the Windhawk mod.
# No other timer is touched; native taskbar state and cursor are restored.
& (Join-Path $PSScriptRoot 'Measure-NativeAutoHideUnlock.ps1') -CompileOnly:$CompileOnly -TemporarilyRestoreRoundedTB -HideTimerProbe -CancelNativeHideTimer:(!$ObserveOnly) -Seconds 5
