param([switch]$CompileOnly,[ValidateSet(0,2)][uint32]$ExpansionFlags=0,[switch]$LockMaximizedWindow)
$ErrorActionPreference='Stop'
# Reuses the default-mode UI transition and native-state/work-area restoration.
# Remain signed in: this isolates native OFF from the actual unlock sequence.
& (Join-Path $PSScriptRoot 'Measure-NativeAutoHideUnlock.ps1') -CompileOnly:$CompileOnly -TemporarilyRestoreRoundedTB -OffTransition -Seconds 5 -ExpansionFlags $ExpansionFlags -LockMaximizedWindow:$LockMaximizedWindow
