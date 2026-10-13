param([switch]$AssertFixed=$true, [switch]$Chrome, [switch]$WindowOverlay, [switch]$AltTabPreview, [switch]$TaskViewPreview,
 [ValidateSet(1,3,5,7,9)][int]$F11Count=1, [switch]$RepeatSelection, [switch]$NativeFullBar,
 [string]$ChromePath='C:\Program Files (x86)\Google\Chrome\Application\chrome.exe', [string]$ScreenshotPath)
if($WindowOverlay -and !$Chrome){throw '-WindowOverlay requires -Chrome.'}
$selectionPreview=$AltTabPreview -or $TaskViewPreview
if($AltTabPreview -and $TaskViewPreview){throw 'Select either Alt+Tab or Task View.'}
if($selectionPreview -and !$WindowOverlay){throw 'Selection preview requires -WindowOverlay.'}
if($RepeatSelection -and !$selectionPreview){throw '-RepeatSelection requires a selection preview.'}
if($NativeFullBar -and !$selectionPreview){throw '-NativeFullBar requires a selection preview.'}
$ErrorActionPreference='Stop'
if($Chrome -and !(Test-Path $ChromePath)){throw "Chrome executable not found: $ChromePath"}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FullscreenStartProbe {
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h,uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetProp(IntPtr h,string name);
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c,string n);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p,IntPtr a,string c,string n);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo {public uint Size;public Rect Monitor,Work;public uint Flags;}
 [StructLayout(LayoutKind.Sequential)] public struct AppBar {public uint Size;public IntPtr Hwnd;public uint Callback,Edge;public Rect Bounds;public IntPtr Param;}
 [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h,uint f);
 [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr h,ref MonitorInfo m);
 [DllImport("shell32.dll")] public static extern IntPtr SHAppBarMessage(uint m,ref AppBar d);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out Rect r);
 public delegate bool EnumProc(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p,IntPtr l);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h,System.Text.StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern IntPtr GetWindowLong(IntPtr h,int i);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int s);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")] public static extern void keybd_event(byte k,byte s,uint f,UIntPtr e);
 [DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(IntPtr h,out uint c,out byte a,out uint f);
 [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int l,int t,int r,int b);
 [DllImport("user32.dll")] public static extern int GetWindowRgn(IntPtr h,IntPtr r);
 [DllImport("gdi32.dll")] public static extern int GetRgnBox(IntPtr h,out Rect r);
 [DllImport("gdi32.dll")] public static extern bool PtInRegion(IntPtr r,int x,int y);
 [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
}
'@
function Key($k) {[FullscreenStartProbe]::keybd_event($k,0,0,[UIntPtr]::Zero);[FullscreenStartProbe]::keybd_event($k,0,2,[UIntPtr]::Zero)}
function OpenSelection {
 if($TaskViewPreview) {
  [FullscreenStartProbe]::keybd_event(0x5B,0,0,[UIntPtr]::Zero)
  Key 0x09
  [FullscreenStartProbe]::keybd_event(0x5B,0,2,[UIntPtr]::Zero)
 } else {
  [FullscreenStartProbe]::keybd_event(0x12,0,0,[UIntPtr]::Zero)
  Key 0x09
 }
}
function FocusChrome {
 $screen=[Windows.Forms.Screen]::FromHandle($bar).Bounds
 $point=New-Object FullscreenStartProbe+Point
 $point.X=$screen.Left+[int]($screen.Width/2);$point.Y=$screen.Top+100
 $timer=[Diagnostics.Stopwatch]::StartNew()
 do {
  [FullscreenStartProbe]::SetForegroundWindow($chromeHandle)|Out-Null
  if([FullscreenStartProbe]::GetForegroundWindow() -eq $chromeHandle){return}
  [Windows.Forms.Application]::DoEvents()
  if([FullscreenStartProbe]::GetAncestor([FullscreenStartProbe]::WindowFromPoint($point),2) -eq $chromeHandle) {
   [FullscreenStartProbe]::SetCursorPos($point.X,$point.Y)|Out-Null
   [FullscreenStartProbe]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
   [FullscreenStartProbe]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
  }
  Start-Sleep -Milliseconds 50
 } while($timer.Elapsed.TotalSeconds -lt 2)
 if([FullscreenStartProbe]::GetForegroundWindow() -ne $chromeHandle){throw 'Windows blocked activation of the test Chrome window.'}
}
$bar=[FullscreenStartProbe]::FindWindow('Shell_TrayWnd',$null)
$tray=[FullscreenStartProbe]::FindWindowEx($bar,[IntPtr]::Zero,'TrayNotifyWnd',$null)
$form=New-Object Windows.Forms.Form
$form.Text='RoundedTB fullscreen test'
$form.FormBorderStyle=[Windows.Forms.FormBorderStyle]::None
$form.StartPosition=[Windows.Forms.FormStartPosition]::Manual
$form.Bounds=[Windows.Forms.Screen]::FromHandle($bar).Bounds
$form.BackColor=[Drawing.Color]::FromArgb(35,35,35)
$originalCursor=New-Object FullscreenStartProbe+Point
[FullscreenStartProbe]::GetCursorPos([ref]$originalCursor)|Out-Null
$chromeWindow=$null
$expansionToggle=$null
$savedExpansion=$false
$region=[FullscreenStartProbe]::CreateRectRgn(0,0,0,0)
try {
 if($NativeFullBar) {
  Start-Process (Join-Path $env:LOCALAPPDATA 'Programs\RoundedTB\RoundedTB.exe')
  Start-Sleep -Milliseconds 600
  $rtb=Get-Process RoundedTB|Where-Object {$_.MainWindowHandle -ne 0}|Select-Object -First 1
  $rtbRoot=[Windows.Automation.AutomationElement]::FromHandle($rtb.MainWindowHandle)
  $control=$rtbRoot.FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty),'expandWorkAreaCheckBox'))
  $expansionToggle=$control.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
  $savedExpansion=$expansionToggle.Current.ToggleState -eq [Windows.Automation.ToggleState]::On
  if($savedExpansion){$expansionToggle.Toggle();Start-Sleep -Milliseconds 600}
 }
 if($Chrome) {
  $existing=@([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.Condition]::TrueCondition)|ForEach-Object {$_.Current.NativeWindowHandle})
  Start-Process $ChromePath -ArgumentList '--new-window','about:blank'
  for($attempt=0;$attempt -lt 30 -and !$chromeWindow;$attempt++) {
   Start-Sleep -Milliseconds 100
   $chromeWindow=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.Condition]::TrueCondition)|Where-Object {$_.Current.ClassName -eq 'Chrome_WidgetWin_1' -and $existing -notcontains $_.Current.NativeWindowHandle}|Select-Object -First 1
  }
  if(!$chromeWindow){throw 'Temporary Chrome window did not open.'}
  $chromeHandle=[IntPtr]$chromeWindow.Current.NativeWindowHandle
  [FullscreenStartProbe]::ShowWindow($chromeHandle,3)|Out-Null
  FocusChrome
  Start-Sleep -Milliseconds 250
  for($cycle=0;$cycle -lt $F11Count;$cycle++){Key 0x7A;Start-Sleep -Milliseconds 200}
  Start-Sleep -Milliseconds 1000
  $screen=[Windows.Forms.Screen]::FromHandle($bar).Bounds
  $point=New-Object FullscreenStartProbe+Point
  $point.X=$screen.Left+[int]($screen.Width/2);$point.Y=$screen.Bottom-24
  $hit=[FullscreenStartProbe]::GetAncestor([FullscreenStartProbe]::WindowFromPoint($point),2)
  Write-Host ('After F11 count='+$F11Count+' foreground='+[FullscreenStartProbe]::GetForegroundWindow()+' Chrome='+$chromeHandle+' bottom hit='+$hit+' bar='+$bar+' NonRude='+[FullscreenStartProbe]::GetProp($chromeHandle,'NonRudeHWND'))
  if($AssertFixed -and $hit -eq $bar){throw 'Taskbar remains above foreground fullscreen Chrome after F11.'}
 } else {
  $form.Show();$form.Activate();[Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 600
 }
 $monitor=New-Object FullscreenStartProbe+MonitorInfo;$monitor.Size=40
 [FullscreenStartProbe]::GetMonitorInfo([FullscreenStartProbe]::MonitorFromWindow($bar,2),[ref]$monitor)|Out-Null
 $appbar=New-Object FullscreenStartProbe+AppBar;$appbar.Size=[Runtime.InteropServices.Marshal]::SizeOf($appbar);$appbar.Hwnd=$bar
 $state=[FullscreenStartProbe]::SHAppBarMessage(4,[ref]$appbar)
 Write-Host ('Before shell selection: monitor bottom='+$monitor.Monitor.Bottom+' work bottom='+$monitor.Work.Bottom+' native appbar state='+$state)
 $appbar.Edge=3;$appbar.Bounds=$monitor.Monitor
 Write-Host ('Auto-hide bottom bar='+[FullscreenStartProbe]::SHAppBarMessage(7,[ref]$appbar)+' extended='+[FullscreenStartProbe]::SHAppBarMessage(11,[ref]$appbar))
 if($Chrome) {$beforeChrome=New-Object FullscreenStartProbe+Rect;[FullscreenStartProbe]::GetWindowRect($chromeHandle,[ref]$beforeChrome)|Out-Null;Write-Host ('Chrome before selection bottom='+$beforeChrome.Bottom)}
 if($WindowOverlay) {
  $form.FormBorderStyle=[Windows.Forms.FormBorderStyle]::Sizable
  $form.Bounds=New-Object Drawing.Rectangle ($monitor.Monitor.Left+400),($monitor.Monitor.Top+200),600,400
  $form.Show();$form.Activate();[Windows.Forms.Application]::DoEvents()
  if($selectionPreview) {
   if($RepeatSelection) {
    FocusChrome
    Start-Sleep -Milliseconds 300
    OpenSelection
    Start-Sleep -Milliseconds 500
    Key 0x1B
    [FullscreenStartProbe]::keybd_event(0x12,0,2,[UIntPtr]::Zero)
    FocusChrome
    Start-Sleep -Milliseconds 600
   }
   FocusChrome
   Start-Sleep -Milliseconds 300
   OpenSelection
  }
 } else {Key 0x5B}
 $samples=@()
 for($i=0;$i -lt 30;$i++) {
  [Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 50
  $rect=New-Object FullscreenStartProbe+Rect;$trayRect=New-Object FullscreenStartProbe+Rect;$box=New-Object FullscreenStartProbe+Rect
  [FullscreenStartProbe]::GetWindowRect($bar,[ref]$rect)|Out-Null
  [FullscreenStartProbe]::GetWindowRect($tray,[ref]$trayRect)|Out-Null
  $kind=[FullscreenStartProbe]::GetWindowRgn($bar,$region)
  [FullscreenStartProbe]::GetRgnBox($region,[ref]$box)|Out-Null
  [uint32]$processId=0;[FullscreenStartProbe]::GetWindowThreadProcessId([FullscreenStartProbe]::GetForegroundWindow(),[ref]$processId)|Out-Null
  $name=(Get-Process -Id $processId -ErrorAction SilentlyContinue).ProcessName
  $foregroundClass=New-Object Text.StringBuilder 128
  [FullscreenStartProbe]::GetClassName([FullscreenStartProbe]::GetForegroundWindow(),$foregroundClass,128)|Out-Null
  [byte]$alpha=0;[uint32]$color=0;[uint32]$flags=0
  [FullscreenStartProbe]::GetLayeredWindowAttributes($bar,[ref]$color,[ref]$alpha,[ref]$flags)|Out-Null
  $trayVisible=$kind -eq 0 -or [FullscreenStartProbe]::PtInRegion($region,($trayRect.Left+$trayRect.Right)/2-$rect.Left,($trayRect.Top+$trayRect.Bottom)/2-$rect.Top)
  $fullWidth=$kind -eq 0 -or [FullscreenStartProbe]::PtInRegion($region,20,24)
  $samples+=[pscustomobject]@{Ms=($i+1)*50;Foreground=$name;Top=$rect.Top;Bottom=$rect.Bottom;RegionTop=$box.Top;RegionBottom=$box.Bottom;TrayVisible=$trayVisible;Alpha=$alpha;FullWidth=$fullWidth;ForegroundClass=$foregroundClass.ToString()}
 }
 $samples|Format-Table -AutoSize
 if($AssertFixed -and $selectionPreview) {
  $stable=@($samples|Where-Object {$_.Ms -ge 300})
  if(@($stable|Where-Object {$_.ForegroundClass -notin @('XamlExplorerHostIslandWindow','MultitaskingViewFrame','TaskSwitcherWnd')}).Count){throw 'Window selection UI did not remain open.'}
  if(@($stable|Where-Object {!$_.TrayVisible -or $_.Alpha -eq 0}).Count){throw 'Taskbar became hidden during the window selection preview.'}
  if($NativeFullBar -and @($stable|Where-Object {!$_.FullWidth}).Count){throw 'Full-width taskbar did not remain restored during window selection.'}
 }
 [FullscreenStartProbe]::GetMonitorInfo([FullscreenStartProbe]::MonitorFromWindow($bar,2),[ref]$monitor)|Out-Null
 Write-Host ('With shell selection: monitor bottom='+$monitor.Monitor.Bottom+' work bottom='+$monitor.Work.Bottom)
 if($Chrome) {$client=New-Object FullscreenStartProbe+Rect;[FullscreenStartProbe]::GetClientRect($chromeHandle,[ref]$client)|Out-Null;Write-Host ('Chrome client height='+$client.Bottom)}
 if($Chrome) {
  $bounds=[Windows.Forms.Screen]::FromHandle($bar).Bounds
  $bmp=New-Object Drawing.Bitmap $bounds.Width,$bounds.Height
  $g=[Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($bounds.Left,$bounds.Top,0,0,$bmp.Size)
  if($ScreenshotPath){$bmp.Save([IO.Path]::GetFullPath($ScreenshotPath),[Drawing.Imaging.ImageFormat]::Png)}
  if($AssertFixed) {
   $actual=New-Object FullscreenStartProbe+Rect;[FullscreenStartProbe]::GetWindowRect($chromeHandle,[ref]$actual)|Out-Null
   if($actual.Bottom -ne $bounds.Bottom){throw 'Chrome leaves a gap below its background fullscreen window.'}
   if($WindowOverlay -and !$selectionPreview -and [FullscreenStartProbe]::GetForegroundWindow() -ne $form.Handle){throw 'The overlay lost foreground focus.'}
   if(!$TaskViewPreview) {
    for($x=0;$x -lt $bmp.Width;$x++) {
     if($bmp.GetPixel($x,$bmp.Height-1).ToArgb() -ne $bmp.GetPixel($x,$bmp.Height-3).ToArgb()) {
      throw ('Visible bottom-row artifact at x='+$x)
     }
    }
    Write-Host 'Chrome covers the full monitor and the final screen row matches the browser background.'
   } else {
    Write-Host 'Chrome client fills the monitor. Pixel comparison skipped: Task View paints its own desktop background.'
   }
  }
  [FullscreenStartProbe]::EnumWindows({param($h,$unused)
   $wr=New-Object FullscreenStartProbe+Rect
   if([FullscreenStartProbe]::IsWindowVisible($h) -and [FullscreenStartProbe]::GetWindowRect($h,[ref]$wr) -and $wr.Bottom -ge $bounds.Bottom-2) {
    $cls=New-Object Text.StringBuilder 256;[FullscreenStartProbe]::GetClassName($h,$cls,256)|Out-Null
    $kind=[FullscreenStartProbe]::GetWindowRgn($h,$region);$box=New-Object FullscreenStartProbe+Rect
    [FullscreenStartProbe]::GetRgnBox($region,[ref]$box)|Out-Null
    Write-Host ('BottomWindow handle='+$h+' class='+$cls+' rect='+$wr.Left+','+$wr.Top+','+$wr.Right+','+$wr.Bottom+' regionKind='+$kind+' regionBox='+$box.Left+','+$box.Top+','+$box.Right+','+$box.Bottom+' style='+[FullscreenStartProbe]::GetWindowLong($h,-16))
   }
   return $true
  },[IntPtr]::Zero)|Out-Null
  $g.Dispose();$bmp.Dispose()
 }
 if($AssertFixed -and !$WindowOverlay) {
  $start=@($samples|Where-Object {$_.Foreground -in @('StartMenuExperienceHost','SearchHost') -and $_.Ms -ge 800})
  if(!$start.Count){throw 'Start did not open over the fullscreen test window.'}
  if(@($start|Where-Object {!$_.TrayVisible -or $_.Alpha -eq 0}).Count){throw 'Tray was hidden while Start was open.'}
  $screenBottom=$form.Bounds.Bottom
  if(@($start|Where-Object {$_.Top -ge $screenBottom-2}).Count){throw 'Taskbar was still reduced to the fullscreen bottom edge.'}
 }
} finally {
 if($AltTabPreview){[FullscreenStartProbe]::keybd_event(0x12,0,2,[UIntPtr]::Zero)}
 Key 0x1B
 if($chromeWindow){$chromeWindow.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()}
 [FullscreenStartProbe]::SetCursorPos($originalCursor.X,$originalCursor.Y)|Out-Null
 $form.Close();$form.Dispose();[FullscreenStartProbe]::DeleteObject($region)|Out-Null
 if($expansionToggle -and (($expansionToggle.Current.ToggleState -eq [Windows.Automation.ToggleState]::On) -ne $savedExpansion)){$expansionToggle.Toggle()}

}
