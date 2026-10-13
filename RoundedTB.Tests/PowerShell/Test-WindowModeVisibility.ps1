$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WindowModeProbe {
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point p);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr w,int command);
 [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr w);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls,string title);
 [DllImport("user32.dll")] public static extern int GetWindowRgn(IntPtr w,IntPtr r);
 [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int l,int t,int r,int b);
 [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr r);
 public static bool Empty(IntPtr w) { var r=CreateRectRgn(0,0,0,0); try{return GetWindowRgn(w,r)==1;} finally{DeleteObject(r);} }
}
'@
$settings=Get-Content (Join-Path $env:LOCALAPPDATA 'rtb.json') -Raw|ConvertFrom-Json
if($settings.IsWindowsDefault -or !$settings.IsDynamic){throw 'This test requires active rounded areas.'}
Start-Process (Join-Path $env:LOCALAPPDATA 'Programs\RoundedTB\RoundedTB.exe')
Start-Sleep -Milliseconds 800
$p=Get-Process RoundedTB|Where-Object {$_.MainWindowHandle -ne 0}|Select-Object -First 1
$root=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
function Control($id){$root.FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty),$id))}
function Click($id){(Control $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()}
function Toggle($id,$on){$t=(Control $id).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern);if(($t.Current.ToggleState -eq [Windows.Automation.ToggleState]::On) -ne $on){$t.Toggle()}}
function Mode($picker,$normal,$maximized){
 Click 'generalNavigationButton'
 Click $picker
 Click @('visibilityAlwaysButton','visibilityHoverButton','visibilityHiddenButton')[$normal]
 Click @('maximizedVisibilityAlwaysButton','maximizedVisibilityHoverButton','maximizedVisibilityHiddenButton')[$maximized]
}
function SavedMode($layout,$shown){if($layout.ShowOnHover){1}elseif($shown){0}else{2}}
function SavedMax($layout,$normal){if($null -eq $layout.MaximizedVisibility){$normal}else{[int]$layout.MaximizedVisibility}}
$apps=SavedMode $settings.DynamicAppListLayout $true
$tray=SavedMode $settings.DynamicTrayLayout $settings.ShowTray
$widgets=SavedMode $settings.DynamicWidgetsLayout $settings.ShowWidgets
$cursor=New-Object WindowModeProbe+Point
[void][WindowModeProbe]::GetCursorPos([ref]$cursor)
$wasMaximized=[WindowModeProbe]::IsZoomed($p.MainWindowHandle)
$taskbar=[WindowModeProbe]::FindWindow('Shell_TrayWnd',$null)
function WaitRegion($empty){
 $timer=[Diagnostics.Stopwatch]::StartNew()
 do {Start-Sleep -Milliseconds 100;$actual=[WindowModeProbe]::Empty($taskbar)} while($actual -ne $empty -and $timer.Elapsed.TotalSeconds -lt 8)
 if($actual -ne $empty){throw "Taskbar region did not match expected empty=$empty"}
}
try {
 Mode 'taskbarRectStandIn' 0 1
 Click 'notificationsNavigationButton'
 Toggle 'revealOnAppOpenCheckBox' $false
 Toggle 'revealOnAppAttentionCheckBox' $false
 Click 'generalNavigationButton'
 Toggle 'expandWorkAreaCheckBox' $false
 Toggle 'fillMaximisedCheckBox' $false
 Toggle 'fillAltTabCheckBox' $false
 Mode 'trayRectStandIn' 2 2
 if(!(Control 'widgetsRectStandIn').Current.IsOffscreen){Mode 'widgetsRectStandIn' 2 2}
 Mode 'taskbarRectStandIn' 0 1
 [void][WindowModeProbe]::SetCursorPos(100,100)
 [void][WindowModeProbe]::ShowWindow($p.MainWindowHandle,3)
 WaitRegion $true
 Click 'maximizedVisibilityAlwaysButton'
 WaitRegion $false
 Click 'maximizedVisibilityHoverButton'
 WaitRegion $true
 Write-Output 'Real maximized-window visibility switches between Auto-hide and Always visible; normal Apps mode stays Always visible.'
} finally {
 Click 'notificationsNavigationButton'
 Toggle 'revealOnAppOpenCheckBox' ([bool]$settings.RevealOnAppOpen)
 Toggle 'revealOnAppAttentionCheckBox' ([bool]$settings.RevealOnAppAttention)

 Mode 'taskbarRectStandIn' $apps (SavedMax $settings.DynamicAppListLayout $apps)
 Mode 'trayRectStandIn' $tray (SavedMax $settings.DynamicTrayLayout $tray)
 if(!(Control 'widgetsRectStandIn').Current.IsOffscreen){Mode 'widgetsRectStandIn' $widgets (SavedMax $settings.DynamicWidgetsLayout $widgets)}
 Toggle 'expandWorkAreaCheckBox' ([bool]$settings.ExpandMaximizedWorkArea)
 Toggle 'fillMaximisedCheckBox' ([bool]$settings.FillOnMaximise)
 Toggle 'fillAltTabCheckBox' ([bool]$settings.FillOnTaskSwitch)
 [void][WindowModeProbe]::ShowWindow($p.MainWindowHandle,$(if($wasMaximized){3}else{9}))
 [void][WindowModeProbe]::SetCursorPos($cursor.X,$cursor.Y)
 Click 'generalNavigationButton'
 Click 'taskbarRectStandIn'
}
