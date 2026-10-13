param([switch]$LiveDuration)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'SettingsTestHelpers.ps1')
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SegmentProbe {
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)] public struct Point {public int X,Y;}
 [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls,string name);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window,out Rect rect);
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern int GetWindowRgn(IntPtr window,IntPtr region);
 [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int a,int b,int c,int d);
 [DllImport("gdi32.dll")] public static extern bool PtInRegion(IntPtr region,int x,int y);
 [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr region);
}
'@
$settings = Get-Content (Join-Path $env:LOCALAPPDATA 'rtb.json') | ConvertFrom-Json
if ($settings.IsWindowsDefault -or !$settings.IsDynamic -or !$settings.IsCentred) { throw 'This native geometry test requires the centered dynamic layout.' }
Start-Process (Join-Path $env:LOCALAPPDATA 'Programs\RoundedTB\RoundedTB.exe')
Start-Sleep -Milliseconds 500
$process = Get-Process RoundedTB | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$root = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
function SelectSection($id) {
 $condition=New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty),$id
 $root.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Control($id) {
 if($id -in @('fillMaximisedCheckBox','fillAltTabCheckBox','enableRoundedTaskbarCheckBox')){SelectSection 'generalNavigationButton'}
 if($id -in @('revealOnAppOpenCheckBox','appOpenDurationInput','revealOnAppAttentionCheckBox','keepAppAttentionOpenCheckBox','appAttentionDurationInput')){SelectSection 'notificationsNavigationButton'}
 if($id -in @('appAttentionTestStyleComboBox','testAppAttentionButton')){SelectSection 'advancedNavigationButton'}

 $condition=New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty),$id
 return $root.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
}
function Toggle($id,$value) {
 $pattern=(Control $id).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
 if(($pattern.Current.ToggleState -eq [Windows.Automation.ToggleState]::On) -ne $value){$pattern.Toggle()}
}
function Duration($value) {
 (Control 'appOpenDurationInput').GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($value)
}
function WaitForLiveSettings { Start-Sleep -Milliseconds 300 }
SelectSection 'generalNavigationButton'
$bar=[SegmentProbe]::FindWindow('Shell_TrayWnd',$null)
$rect=New-Object SegmentProbe+Rect
[SegmentProbe]::GetWindowRect($bar,[ref]$rect)|Out-Null
$mouse=New-Object SegmentProbe+Point
[SegmentProbe]::GetCursorPos([ref]$mouse)|Out-Null
$region=[SegmentProbe]::CreateRectRgn(0,0,0,0)
$x=[int](($rect.Right-$rect.Left)/2);$y=[int](($rect.Bottom-$rect.Top)/2)
function AppsVisible {
 [SegmentProbe]::GetWindowRgn($bar,$region)|Out-Null
 return [SegmentProbe]::PtInRegion($region,$x,$y)
}
$calculator=$null
try {
 Toggle 'fillMaximisedCheckBox' $false
 Set-TestSegmentProfile $root 'Taskbar' (New-TestInstantHoverProfile) 1 1
 Toggle 'revealOnAppAttentionCheckBox' $false
 Toggle 'revealOnAppOpenCheckBox' $true
 Duration 2
 WaitForLiveSettings
 [SegmentProbe]::SetCursorPos($rect.Left+$x,$rect.Top-200)|Out-Null
 Start-Sleep -Milliseconds 500
 if(AppsVisible){throw 'App segment did not hide away from the taskbar'}
 SelectSection 'appearanceNavigationButton'
 (Control 'taskbarRectStandIn').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
 if(!(Control 'hoverDelayInput').Current.IsEnabled){throw 'Hover controls are not enabled for the auto-hidden taskbar'}
 SelectSection 'generalNavigationButton'
 $existing=@([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.Condition]::TrueCondition)|ForEach-Object{$_.Current.NativeWindowHandle})
 $timer=[Diagnostics.Stopwatch]::StartNew()
 Start-Process calc.exe
 $shownAt=-1
 while($timer.Elapsed.TotalSeconds -lt 15){
  if(AppsVisible){$shownAt=$timer.ElapsedMilliseconds;break}
  Start-Sleep -Milliseconds 10
 }
 if($shownAt -lt 0){throw 'Opening Calculator did not reveal the auto-hidden taskbar'}
 $shown=[Diagnostics.Stopwatch]::StartNew()
 $calculator=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -match 'Calculator' -and $existing -notcontains $_.Current.NativeWindowHandle}|Select-Object -First 1
 if(!$calculator){throw 'Could not identify the newly opened Calculator window'}
 if($LiveDuration){Duration 0.5}
 while((AppsVisible) -and $shown.Elapsed.TotalSeconds -lt 5){Start-Sleep -Milliseconds 10}
 $duration=$shown.ElapsedMilliseconds
 $minimum=if($LiveDuration){300}else{1700}
 $maximum=if($LiveDuration){900}else{2400}
 if($duration -lt $minimum -or $duration -gt $maximum){throw "Reveal duration was $duration ms; expected range $minimum to $maximum ms"}
 Write-Output "App-open reveal duration: $duration ms. Visibility and hover controls synchronized."
} finally {
 if($calculator){$calculator.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()}
 SelectSection 'generalNavigationButton'
 # Restore numeric values while their controlling settings remain enabled.
 Duration ([double]$settings.AppOpenRevealSeconds)
 Toggle 'revealOnAppOpenCheckBox' ([bool]$settings.RevealOnAppOpen)
 Toggle 'revealOnAppAttentionCheckBox' ([bool]$settings.RevealOnAppAttention)
 $savedNormal=[int][bool]$settings.DynamicAppListLayout.ShowOnHover
 Set-TestSegmentProfile $root 'Taskbar' $settings.DynamicAppListLayout $savedNormal (Get-TestMaximizedVisibility $settings.DynamicAppListLayout $savedNormal)
 Toggle 'fillMaximisedCheckBox' ([bool]$settings.FillOnMaximise)
 WaitForLiveSettings
 [SegmentProbe]::SetCursorPos($mouse.X,$mouse.Y)|Out-Null
 [SegmentProbe]::DeleteObject($region)|Out-Null
}
