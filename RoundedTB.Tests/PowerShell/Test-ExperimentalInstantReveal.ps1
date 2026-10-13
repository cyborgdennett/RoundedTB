param([switch]$AlwaysShow, [string]$ChromePath='C:\Program Files (x86)\Google\Chrome\Application\chrome.exe')
$ErrorActionPreference='Stop'
if(!(Test-Path $ChromePath)){throw "Chrome executable not found: $ChromePath"}
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HoverProbe {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetProp(IntPtr h,string n);
 [DllImport("user32.dll")] public static extern IntPtr GetWindowLong(IntPtr h,int i);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h,uint flags);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);

 [DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(IntPtr h,out uint color,out byte alpha,out uint flags);
 [StructLayout(LayoutKind.Sequential)] public struct Placement {public int Length,Flags,Show;public Point Min,Max;public Rect Normal;}
 [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr h,ref Placement p);
 [StructLayout(LayoutKind.Sequential)] public struct Monitor {public int Size;public Rect Bounds,Work;public uint Flags;}
 [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
 [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr h,ref Monitor m);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int n);
 [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h,int a,out Rect r,int size);
 [DllImport("user32.dll")] public static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
 [DllImport("user32.dll")] public static extern IntPtr FindWindow(string c,string n);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point p);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern int GetWindowRgn(IntPtr h,IntPtr r);
 [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int a,int b,int c,int d);
 [DllImport("gdi32.dll")] public static extern bool PtInRegion(IntPtr r,int x,int y);
 [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr r);
}
'@
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'SettingsTestHelpers.ps1')
$watch=[Diagnostics.Stopwatch]::StartNew();$root=$null
while($watch.Elapsed.TotalSeconds -lt 20) {
 $p=Get-Process RoundedTB -ErrorAction SilentlyContinue|Select-Object -First 1
 if($p -and $p.MainWindowHandle -ne 0){$root=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle);break}
 Start-Sleep -Milliseconds 100
}
if(!$root){throw 'Configuration window unavailable'}
function SelectSection($id) {
 $condition=New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty),$id
 $tab=$root.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
 $tab.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Control($id) {
 if($id -in @('fillMaximisedCheckBox','fillAltTabCheckBox','enableRoundedTaskbarCheckBox')){SelectSection 'generalNavigationButton'}
 if($id -eq 'expandWorkAreaCheckBox'){SelectSection 'generalNavigationButton'}

 $c=New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty),$id
 return $root.FindFirst([Windows.Automation.TreeScope]::Descendants,$c)
}
$bar=[HoverProbe]::FindWindow('Shell_TrayWnd',$null)
$barRect=New-Object HoverProbe+Rect;$originalCursor=New-Object HoverProbe+Point
[HoverProbe]::GetWindowRect($bar,[ref]$barRect)|Out-Null
[HoverProbe]::GetCursorPos([ref]$originalCursor)|Out-Null
$r=[HoverProbe]::CreateRectRgn(0,0,0,0)
function ToggleTo($pattern,$value) {if(($pattern.Current.ToggleState -eq [Windows.Automation.ToggleState]::On) -ne $value){$pattern.Toggle()}}
function Work {
 $m=New-Object HoverProbe+Monitor;$m.Size=40
 [HoverProbe]::GetMonitorInfo([HoverProbe]::MonitorFromWindow($bar,2),[ref]$m)|Out-Null
 return $m
}
function WaitForLiveSettings { Start-Sleep -Milliseconds 300 }
function FocusBrowser {
 [HoverProbe]::SetForegroundWindow($browserHandle)|Out-Null
 if([HoverProbe]::GetForegroundWindow() -eq $browserHandle){return}
 $point=New-Object HoverProbe+Point
 $point.X=$screenCenter;$point.Y=$expanded.Bounds.Top+100
 [HoverProbe]::SetCursorPos($point.X,$point.Y)|Out-Null
 if([HoverProbe]::GetAncestor([HoverProbe]::WindowFromPoint($point),2) -ne $browserHandle){throw 'Chrome is covered; cannot safely activate the test window.'}
 [HoverProbe]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
 [HoverProbe]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 100
 if([HoverProbe]::GetForegroundWindow() -ne $browserHandle){throw 'Windows blocked activation of the test Chrome window.'}
}
$settings=Get-Content (Join-Path $env:LOCALAPPDATA 'rtb.json')|ConvertFrom-Json
if($settings.IsWindowsDefault -or !$settings.IsDynamic -or !$settings.IsCentred){throw 'Requires active RoundedTB with centered separate segments.'}
$savedExpansion=[bool]$settings.ExpandMaximizedWorkArea
$savedFill=[bool]$settings.FillOnMaximise
$savedApps=[bool]$settings.DynamicAppListLayout.ShowOnHover
$savedTray=[bool]$settings.DynamicTrayLayout.ShowOnHover
$checkbox=(Control 'expandWorkAreaCheckBox').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
function SetExpansion($enabled) {
 SelectSection 'generalNavigationButton'
 ToggleTo $checkbox $enabled
}
function HideMode($index) {
 Set-TestSegmentProfile $root 'Taskbar' (New-TestInstantHoverProfile) $index $index
 Set-TestSegmentProfile $root 'Tray' (New-TestInstantHoverProfile) $index $index
}
function RestoreHideMode {
 $apps=[int]$savedApps
 $tray=if($savedTray){1}elseif($settings.ShowTray){0}else{2}
 Set-TestSegmentProfile $root 'Taskbar' $settings.DynamicAppListLayout $apps (Get-TestMaximizedVisibility $settings.DynamicAppListLayout $apps)
 Set-TestSegmentProfile $root 'Tray' $settings.DynamicTrayLayout $tray (Get-TestMaximizedVisibility $settings.DynamicTrayLayout $tray)
}
$browserWindow=$null
try {
 SetExpansion $false
 WaitForLiveSettings
 $originalWork=Work
 HideMode $(if($AlwaysShow){0}else{1})
 SetExpansion $true
 WaitForLiveSettings
 $expanded=Work
 $barWidth=$barRect.Right-$barRect.Left
 $screenCenter=$expanded.Bounds.Left+[int](($expanded.Bounds.Right-$expanded.Bounds.Left)/2)
 $appX=$screenCenter-$barRect.Left
 $appY=[int](($barRect.Bottom-$barRect.Top)/2)
 [pscustomobject]@{MonitorBottom=$expanded.Bounds.Bottom;ExpandedWorkBottom=$expanded.Work.Bottom;OriginalWorkBottom=$originalWork.Work.Bottom}|ConvertTo-Json
 if($expanded.Work.Bottom -ne $expanded.Bounds.Bottom){throw 'Maximized work area not expanded'}
 $existing=@([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.Condition]::TrueCondition)|ForEach-Object {$_.Current.NativeWindowHandle})
 Start-Process $ChromePath -ArgumentList '--new-window','about:blank'
 Start-Sleep -Seconds 2
 $browserWindow=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.Condition]::TrueCondition)|Where-Object {$_.Current.ClassName -eq 'Chrome_WidgetWin_1' -and $existing -notcontains $_.Current.NativeWindowHandle}|Select-Object -First 1
 if(!$browserWindow){throw 'Temporary Chrome window unavailable'}
 $browserHandle=[IntPtr]$browserWindow.Current.NativeWindowHandle
 [HoverProbe]::ShowWindow($browserHandle,3)|Out-Null
 Start-Sleep -Milliseconds 5000
 $placement=New-Object HoverProbe+Placement;$placement.Length=44
 [HoverProbe]::GetWindowPlacement($browserHandle,[ref]$placement)|Out-Null
 Write-Host ('ShowState='+$placement.Show)
 $now=Work;Write-Host ('WorkAfterMaximize='+$now.Work.Bottom)
 $active=Get-Content (Join-Path $env:LOCALAPPDATA 'rtb.json')|ConvertFrom-Json;Write-Host ('SavedExpand='+$active.ExpandMaximizedWorkArea+' AutoHideTaskbar='+$active.DynamicAppListLayout.ShowOnHover)
 $bounds=New-Object HoverProbe+Rect
 $result=[HoverProbe]::DwmGetWindowAttribute($browserHandle,9,[ref]$bounds,16)
 if($result -ne 0){[HoverProbe]::GetWindowRect($browserHandle,[ref]$bounds)|Out-Null}
 Write-Host ("ChromeHandle="+$browserHandle+" DwmResult="+$result)
 [pscustomobject]@{MaximizedChromeBottom=$bounds.Bottom;MonitorBottom=$expanded.Bounds.Bottom}|ConvertTo-Json
 if($bounds.Bottom -lt $expanded.Work.Bottom){throw 'Maximized Chrome does not reach screen bottom'}
 $transitions=0;$lastBottom=$bounds.Bottom
 for($i=0;$i -lt 40;$i++) {
  Start-Sleep -Milliseconds 100
  [HoverProbe]::DwmGetWindowAttribute($browserHandle,9,[ref]$bounds,16)|Out-Null
  if($bounds.Bottom -ne $lastBottom){$transitions++;$lastBottom=$bounds.Bottom}
 }
 Write-Host ('NonRudeMark='+[HoverProbe]::GetProp($browserHandle,'NonRudeHWND'))
 if([HoverProbe]::GetProp($browserHandle,'NonRudeHWND') -ne [IntPtr]1){throw 'Maximized Chrome was not marked'}
 Write-Host ('MaximizedSizeTransitions='+$transitions)
 if($transitions -ne 0){throw 'Maximized window size is unstable'}
 if($AlwaysShow) {
  [HoverProbe]::SetCursorPos($screenCenter,$expanded.Bounds.Top+100)|Out-Null
  Start-Sleep -Milliseconds 1200
  $shown=New-Object HoverProbe+Rect
  [HoverProbe]::GetWindowRect($bar,[ref]$shown)|Out-Null
  $color=[uint32]0;$alpha=[byte]0;$flags=[uint32]0
  [HoverProbe]::GetLayeredWindowAttributes($bar,[ref]$color,[ref]$alpha,[ref]$flags)|Out-Null
  Write-Host ('AlwaysShowOpacityAwayFromTaskbar='+$alpha)
  Write-Host ('AlwaysShowTaskbarTop='+$shown.Top)
  if($alpha -ne 255 -or $shown.Bottom -ne $expanded.Bounds.Bottom){throw 'Always show did not keep taskbar visible'}
  $stable=[Diagnostics.Stopwatch]::StartNew()
  $positions=New-Object 'System.Collections.Generic.HashSet[int]'
  while($stable.ElapsedMilliseconds -lt 15000) {
   $sample=New-Object HoverProbe+Rect
   [HoverProbe]::GetWindowRect($bar,[ref]$sample)|Out-Null
   [void]$positions.Add($sample.Top)
   [HoverProbe]::GetLayeredWindowAttributes($bar,[ref]$color,[ref]$alpha,[ref]$flags)|Out-Null
   if($sample.Top -ne $shown.Top -or $alpha -ne 255){throw 'Always show retracted or became transparent during stability check'}
   Start-Sleep -Milliseconds 10
  }
  Write-Host ('AlwaysShowStablePositions='+($positions -join ','))

  if(([HoverProbe]::GetWindowLong($bar,-20).ToInt64() -band 0x20) -ne 0){throw 'Always show left taskbar click-through'}
  if((Work).Work.Bottom -ne $expanded.Bounds.Bottom){throw 'Always show did not retain the expanded work area'}
 } else {
 [HoverProbe]::SetCursorPos($expanded.Bounds.Left+20,$expanded.Bounds.Bottom-1)|Out-Null
 Start-Sleep -Milliseconds 1600
 $empty=New-Object HoverProbe+Rect
 [HoverProbe]::GetWindowRect($bar,[ref]$empty)|Out-Null
 Write-Host ('TaskbarTopAtEmptyEdge='+$empty.Top)
 $color=[uint32]0;$alpha=[byte]0;$flags=[uint32]0
 [HoverProbe]::GetLayeredWindowAttributes($bar,[ref]$color,[ref]$alpha,[ref]$flags)|Out-Null
 Write-Host ('EmptyEdgeOpacity='+$alpha)
 [HoverProbe]::GetWindowRgn($bar,$r)|Out-Null
 if([HoverProbe]::PtInRegion($r,$appX,$appY)){throw 'Auto-hidden app segment remains visible'}
 $target=$screenCenter
 $timer=[Diagnostics.Stopwatch]::StartNew()
 [HoverProbe]::SetCursorPos($target,$empty.Top + [int](($empty.Bottom-$empty.Top)/2))|Out-Null
 $shownAt=-1
 $tops=@()
 for($j=0;$j -lt 100;$j++) {
  $color=[uint32]0;$alpha=[byte]0;$flags=[uint32]0
  [HoverProbe]::GetLayeredWindowAttributes($bar,[ref]$color,[ref]$alpha,[ref]$flags)|Out-Null
  $pos=New-Object HoverProbe+Rect
  [HoverProbe]::GetWindowRect($bar,[ref]$pos)|Out-Null
  $tops+=$pos.Top
  [HoverProbe]::GetWindowRgn($bar,$r)|Out-Null
  if($alpha -eq 255 -and [HoverProbe]::PtInRegion($r,$appX,$appY) -and $shownAt -lt 0){$shownAt=$timer.ElapsedMilliseconds}
  Start-Sleep -Milliseconds 5
 }
 Write-Host ('RevealLatencyMs='+$shownAt)
 Write-Host ('TaskbarPositions='+($tops|Select-Object -Unique))
 if($shownAt -lt 0 -or $shownAt -gt 150){throw 'Experimental reveal remains delayed'}
 if(@($tops|Select-Object -Unique).Count -ne 1){throw 'Native taskbar still slides during reveal'}
 $lineFrames=0
 for($i=0;$i -lt 100;$i++) {
  $color=[uint32]0;$alpha=[byte]0;$flags=[uint32]0
  [HoverProbe]::GetLayeredWindowAttributes($bar,[ref]$color,[ref]$alpha,[ref]$flags)|Out-Null
  [HoverProbe]::GetWindowRgn($bar,$r)|Out-Null
  if($alpha -eq 255 -and [HoverProbe]::PtInRegion($r,0,0) -and [HoverProbe]::PtInRegion($r,$barWidth-1,1) -and ![HoverProbe]::PtInRegion($r,0,4)) {$lineFrames++}
  Start-Sleep -Milliseconds 10
 }
 Write-Host ('VisibleActivationLineFrames='+$lineFrames)
 if($lineFrames -ne 0){throw 'Visible activation line during reveal'}
 Start-Sleep -Milliseconds 200
 $color=[uint32]0;$alpha=[byte]0;$flags=[uint32]0
 [HoverProbe]::GetLayeredWindowAttributes($bar,[ref]$color,[ref]$alpha,[ref]$flags)|Out-Null
 Write-Host ('RevealedOpacity='+$alpha)
 if($alpha -ne 255){throw 'Taskbar did not become opaque'}
 $revealed=New-Object HoverProbe+Rect
 [HoverProbe]::GetWindowRect($bar,[ref]$revealed)|Out-Null
 [HoverProbe]::DwmGetWindowAttribute($browserHandle,9,[ref]$bounds,16)|Out-Null
 [pscustomobject]@{TaskbarTopOnHover=$revealed.Top;TaskbarBottomOnHover=$revealed.Bottom;ChromeBottomWhileTaskbarOpen=$bounds.Bottom}|ConvertTo-Json
 if($revealed.Bottom -gt $expanded.Bounds.Bottom -or $bounds.Bottom -lt $expanded.Work.Bottom){throw 'Taskbar reveal check failed'}
 }
 FocusBrowser
 [HoverProbe]::keybd_event(0x7A,0,0,[UIntPtr]::Zero);[HoverProbe]::keybd_event(0x7A,0,2,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 1500
 Write-Host ('FullscreenMark='+[HoverProbe]::GetProp($browserHandle,'NonRudeHWND'))
 Write-Host ('FullscreenStyle='+[HoverProbe]::GetWindowLong($browserHandle,-16))
 if([HoverProbe]::GetProp($browserHandle,'NonRudeHWND') -ne [IntPtr]0){throw 'Fullscreen marking was not removed'}
 [HoverProbe]::keybd_event(0x7A,0,0,[UIntPtr]::Zero);[HoverProbe]::keybd_event(0x7A,0,2,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 1000
 SetExpansion $false
 RestoreHideMode
 WaitForLiveSettings
 $restored=Work
 [pscustomobject]@{RestoredWorkBottom=$restored.Work.Bottom;Expected=$originalWork.Work.Bottom}|ConvertTo-Json
 if($restored.Work.Bottom -ne $originalWork.Work.Bottom){throw 'Original work area not restored'}
 [HoverProbe]::DwmGetWindowAttribute($browserHandle,9,[ref]$bounds,16)|Out-Null
 Write-Host ('RestoredChromeBottom='+$bounds.Bottom)
 if([HoverProbe]::GetProp($browserHandle,'NonRudeHWND') -ne [IntPtr]0 -or [HoverProbe]::GetProp($browserHandle,'RoundedTB.NonRudeOwner') -ne [IntPtr]0){throw 'Window marking cleanup failed'}
 if($bounds.Bottom -ne $originalWork.Work.Bottom){throw 'Maximized app size not restored'}
 if($AlwaysShow) {
  SetExpansion $true
  HideMode 0
  WaitForLiveSettings
  Start-Sleep -Milliseconds 1500
  [HoverProbe]::DwmGetWindowAttribute($browserHandle,9,[ref]$bounds,16)|Out-Null
  Write-Host ('ExistingMaximizedChromeBottomAfterEnable='+$bounds.Bottom)
  if($bounds.Bottom -ne $expanded.Bounds.Bottom){throw 'Already maximized app did not expand when applying Always show'}
 }

} finally {
 if($browserWindow){$browserWindow.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()}
 RestoreHideMode
 SetExpansion $savedExpansion
 ToggleTo ((Control 'fillMaximisedCheckBox').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)) $savedFill
 WaitForLiveSettings
 [HoverProbe]::SetCursorPos($originalCursor.X,$originalCursor.Y)|Out-Null
 [HoverProbe]::DeleteObject($r)|Out-Null
}
