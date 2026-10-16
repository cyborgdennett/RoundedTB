param([string]$AppDirectory=(Join-Path $env:LOCALAPPDATA 'Programs\RoundedTB'),[ValidateRange(30,420)][int]$TimeoutSeconds=330)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SymbolResolutionProbe {
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)] public struct Appbar {public uint Size;public IntPtr Window;public uint Callback,Edge;public Rect Bounds;public IntPtr Param;}
 [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls,string name);
 [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint message,ref Appbar data);
 public static bool NativeHide(){var d=new Appbar{Size=(uint)Marshal.SizeOf(typeof(Appbar)),Window=FindWindow("Shell_TrayWnd",null)};return (SHAppBarMessage(4,ref d).ToUInt64()&1)!=0;}
}
'@
. (Join-Path $PSScriptRoot 'SettingsTestHelpers.ps1')
$cache=Join-Path $env:LOCALAPPDATA 'RoundedTB\NativeSymbols\Resolved-v1'
if(Test-Path $cache){if(@(Get-ChildItem $cache -Filter '*.json').Count){throw 'Cold-download test requires an empty automatic symbol cache. Preserve any existing cache before running this test.'}}
$settings=Get-Content (Join-Path $env:LOCALAPPDATA 'rtb.json') -Raw|ConvertFrom-Json
if($settings.IsWindowsDefault -or !$settings.IsDynamic){throw 'Test requires active rounded taskbar segments.'}
Start-Process (Join-Path $AppDirectory 'RoundedTB.exe')
Start-Sleep -Seconds 1
$p=Get-Process RoundedTB|Where-Object {$_.MainWindowHandle -ne 0}|Select-Object -First 1
if(!$p){throw 'Settings window unavailable.'}
$root=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
function Toggle($id,$on){$t=(Find-TestSettingsControl $root $id).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern);if(($t.Current.ToggleState -eq [Windows.Automation.ToggleState]::On) -ne $on){$t.Toggle()}}
$packaged=Join-Path $AppDirectory 'Native\symbols'
$backup=$packaged+'.test-'+[guid]::NewGuid().ToString('N')
$log=Join-Path $env:LOCALAPPDATA 'rtb.log'
try {
 Invoke-TestSettingsControl $root 'generalNavigationButton'
 Toggle 'expandWorkAreaCheckBox' $false
 Toggle 'fillMaximisedCheckBox' $false
 Start-Sleep -Seconds 1
 if([SymbolResolutionProbe]::NativeHide()){throw 'Native helper did not stop before the test.'}
 if(Test-Path $packaged){Move-Item $packaged $backup}
 $offset=(Get-Item $log).Length
 Toggle 'expandWorkAreaCheckBox' $true
 # The UI must remain usable while network resolution is in progress.
 Invoke-TestSettingsControl $root 'advancedNavigationButton'
 Invoke-TestSettingsControl $root 'generalNavigationButton'
 $watch=[Diagnostics.Stopwatch]::StartNew()
 do {
  Start-Sleep -Milliseconds 200
  $stream=[IO.File]::Open($log,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
  try {$stream.Position=$offset;$reader=New-Object IO.StreamReader $stream;$tail=$reader.ReadToEnd()}finally{$stream.Dispose()}
  if($tail -match 'symbol resolution unavailable'){throw $tail}
 } while(($tail -notmatch 'symbols verified and cached' -or ![SymbolResolutionProbe]::NativeHide()) -and $watch.Elapsed.TotalSeconds -lt $TimeoutSeconds)
 if($tail -notmatch 'symbols verified and cached' -or ![SymbolResolutionProbe]::NativeHide()){throw 'Automatic download/attach timed out.'}
 if(@(Get-ChildItem $cache -Filter '*.json').Count -ne 2){throw 'Expected two verified module cache records.'}
 if(@(Get-ChildItem $cache -Filter '*.tmp*').Count){throw 'Temporary symbol downloads remain after resolution.'}
 Write-Output 'Cold Microsoft symbol download and automatic native attachment passed; settings remained responsive.'
 Toggle 'expandWorkAreaCheckBox' $false
 Start-Sleep -Seconds 1
 $offset=(Get-Item $log).Length
 Toggle 'expandWorkAreaCheckBox' $true
 Start-Sleep -Seconds 2
 if(![SymbolResolutionProbe]::NativeHide()){throw 'Cached native attachment failed.'}
 $stream=[IO.File]::Open($log,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
 try {$stream.Position=$offset;$reader=New-Object IO.StreamReader $stream;$tail=$reader.ReadToEnd()}finally{$stream.Dispose()}
 if($tail -match 'Resolving exact Windows'){throw 'Cache reuse unexpectedly started another download.'}
 Write-Output 'Verified cache reused without a new symbol download.'
} finally {
 Toggle 'expandWorkAreaCheckBox' $false
 if(Test-Path $backup){Move-Item $backup $packaged}
 Toggle 'expandWorkAreaCheckBox' ([bool]$settings.ExpandMaximizedWorkArea)
 Toggle 'fillMaximisedCheckBox' ([bool]$settings.FillOnMaximise)
}
