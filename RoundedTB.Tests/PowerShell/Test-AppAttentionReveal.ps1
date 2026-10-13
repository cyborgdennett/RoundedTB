param([switch]$UseRoundedTBButton,[switch]$KeepOpen, [ValidateRange(0,2)][int]$TestStyle=0)
$ErrorActionPreference = 'Stop'
if(($KeepOpen -or $TestStyle -ne 0) -and !$UseRoundedTBButton){throw '-KeepOpen and -TestStyle require -UseRoundedTBButton.'}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'SettingsTestHelpers.ps1')
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class AttentionProbe {
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)] public struct Point {public int X,Y;}
 [StructLayout(LayoutKind.Sequential)] public struct FlashInfo {public uint Size;public IntPtr Window;public uint Flags,Count,Timeout;}
 [DllImport("user32.dll")] public static extern bool FlashWindowEx(ref FlashInfo info);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
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
if ($settings.IsWindowsDefault -or !$settings.IsDynamic -or !$settings.IsCentred) { throw 'Requires the centered dynamic test layout.' }
Start-Process (Join-Path $env:LOCALAPPDATA 'Programs\RoundedTB\RoundedTB.exe')
Start-Sleep -Milliseconds 500
$process = Get-Process RoundedTB | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$root = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
$originalWindowState = $root.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Current.WindowVisualState
$originalStyle = $null
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
 (Control 'appAttentionDurationInput').GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($value)
}
function WaitForLiveSettings { Start-Sleep -Milliseconds 300 }
SelectSection 'generalNavigationButton'
$bar=[AttentionProbe]::FindWindow('Shell_TrayWnd',$null)
$rect=New-Object AttentionProbe+Rect
[AttentionProbe]::GetWindowRect($bar,[ref]$rect)|Out-Null
$mouse=New-Object AttentionProbe+Point
[AttentionProbe]::GetCursorPos([ref]$mouse)|Out-Null
$region=[AttentionProbe]::CreateRectRgn(0,0,0,0)
$x=[int](($rect.Right-$rect.Left)/2);$y=[int](($rect.Bottom-$rect.Top)/2)
function AppsVisible {
 [Windows.Forms.Application]::DoEvents()
 [AttentionProbe]::GetWindowRgn($bar,$region)|Out-Null
 return [AttentionProbe]::PtInRegion($region,$x,$y)
}
$form=$null
try {
 Toggle 'fillMaximisedCheckBox' $false
 Set-TestSegmentProfile $root 'Taskbar' (New-TestInstantHoverProfile) 1 1
 Toggle 'revealOnAppOpenCheckBox' $false
 Toggle 'revealOnAppAttentionCheckBox' $true
 Toggle 'keepAppAttentionOpenCheckBox' $false
 Duration 2
 Toggle 'keepAppAttentionOpenCheckBox' ([bool]$KeepOpen)
 WaitForLiveSettings
 [AttentionProbe]::SetCursorPos($rect.Left+$x,$rect.Top-200)|Out-Null
 if($UseRoundedTBButton){
  Start-Sleep -Milliseconds 500
  if(AppsVisible){throw 'Taskbar was already revealed before pressing Test.'}
  $combo=Control 'appAttentionTestStyleComboBox'
  $originalStyle=$combo.GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].Current.Name
  $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
  Start-Sleep -Milliseconds 100
  $items=$combo.FindAll([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::ControlTypeProperty),([Windows.Automation.ControlType]::ListItem)))
  $label=@('Normal','Warning (yellow)','Error (red)')[$TestStyle]
  $item=$items|Where-Object {$_.Current.Name -eq $label}|Select-Object -First 1
  if(!$item){throw "Attention style missing: $label"}
  $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
  $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
  Start-Sleep -Milliseconds 200
  (Control 'testAppAttentionButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
  $timer=[Diagnostics.Stopwatch]::StartNew()
  while(!(AppsVisible) -and $timer.Elapsed.TotalSeconds -lt 5){Start-Sleep -Milliseconds 10}
  if(!(AppsVisible)){throw 'RoundedTB Test button did not reveal the taskbar.'}
  if([AttentionProbe]::GetForegroundWindow() -eq $process.MainWindowHandle){throw 'Test left RoundedTB focused.'}
  Start-Sleep -Milliseconds 3500
  if($KeepOpen -and !(AppsVisible)){throw 'Persistent test reveal expired before returning to RoundedTB.'}
  if(!$KeepOpen -and (AppsVisible)){throw 'Continuous flashing extended the two-second reveal timeout.'}
  Write-Output ('Test style '+$TestStyle+' released focus and respected notification visibility. Keep open: '+$KeepOpen)
  return
 }
 $form=New-Object Windows.Forms.Form
 $form.Text='RoundedTB flash notification test'
 $form.TopMost=$true
 $form.Show()
 [Windows.Forms.Application]::DoEvents()
 if(![AttentionProbe]::SetForegroundWindow($process.MainWindowHandle)){throw 'Could not activate settings before the flash test.'}
 Start-Sleep -Milliseconds 500
 if(AppsVisible){throw 'Ordinary app activity revealed the taskbar without a flash.'}
 $flash=New-Object AttentionProbe+FlashInfo
 $flash.Size=[Runtime.InteropServices.Marshal]::SizeOf($flash)
 $flash.Window=$form.Handle;$flash.Flags=2;$flash.Count=1;$flash.Timeout=200
 [AttentionProbe]::FlashWindowEx([ref]$flash)|Out-Null
 $timer=[Diagnostics.Stopwatch]::StartNew()
 while(!(AppsVisible) -and $timer.Elapsed.TotalSeconds -lt 3){Start-Sleep -Milliseconds 10}
 if(!(AppsVisible)){throw 'Native taskbar flashing did not reveal the app segment.'}
 Write-Output ('Flash reveal latency: '+$timer.ElapsedMilliseconds+' ms')
 $timer.Restart()
 while((AppsVisible) -and $timer.Elapsed.TotalSeconds -lt 4){Start-Sleep -Milliseconds 10}
 if((AppsVisible) -or $timer.ElapsedMilliseconds -lt 1700){throw 'Notification reveal did not respect the configured two-second duration.'}
 Write-Output ('Flash reveal duration: '+$timer.ElapsedMilliseconds+' ms')
 # Repeated flashes from an unaccepted app deliberately do not restart the
 # timeout. Accept the first notification before checking a new notification.
 if(![AttentionProbe]::SetForegroundWindow($form.Handle)) {
  Write-Output '[NOT VERIFIED] Second notification and activation: Windows blocked automated activation.'
  return
 }
 Start-Sleep -Milliseconds 300
 if(![AttentionProbe]::SetForegroundWindow($process.MainWindowHandle)) {
  Write-Output '[NOT VERIFIED] Second notification: Windows blocked returning focus to settings.'
  return
 }
 Start-Sleep -Milliseconds 300
 $flash.Flags=0
 [AttentionProbe]::FlashWindowEx([ref]$flash)|Out-Null
 $flash.Flags=2;$flash.Count=1
 [AttentionProbe]::FlashWindowEx([ref]$flash)|Out-Null
 $timer.Restart()
 while(!(AppsVisible) -and $timer.Elapsed.TotalSeconds -lt 3){Start-Sleep -Milliseconds 10}
 if(!(AppsVisible)){throw 'Second flash did not reveal the taskbar.'}
 $activated=[AttentionProbe]::SetForegroundWindow($form.Handle)
 Write-Output ('Programmatic activation accepted: '+$activated)
 if(!$activated){
  $bounds=[Windows.Automation.AutomationElement]::FromHandle($form.Handle).Current.BoundingRectangle
  [AttentionProbe]::SetCursorPos([int]($bounds.Left+$bounds.Width/2),[int]($bounds.Top+$bounds.Height/2))|Out-Null
  [AttentionProbe]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
  [AttentionProbe]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
  [Windows.Forms.Application]::DoEvents()
 }
 $activateTimer=[Diagnostics.Stopwatch]::StartNew()
 while([AttentionProbe]::GetForegroundWindow() -ne $form.Handle -and $activateTimer.Elapsed.TotalSeconds -lt 1){
  [Windows.Forms.Application]::DoEvents()
  Start-Sleep -Milliseconds 10
 }
 if([AttentionProbe]::GetForegroundWindow() -ne $form.Handle){
  Write-Output '[NOT VERIFIED] Activation dismissal: Windows blocked automated activation of the test window.'
 } else {
  $timer.Restart()
  while((AppsVisible) -and $timer.Elapsed.TotalSeconds -lt 1){Start-Sleep -Milliseconds 10}
  if(AppsVisible){throw 'Activating the flashing window did not dismiss notification reveal.'}
  Write-Output 'Activating the app dismissed its reveal.'
 }
} finally {
 if($form){$form.Close();$form.Dispose()}
 $root.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal)
 if($null -ne $originalStyle) {
  $combo=Control 'appAttentionTestStyleComboBox'
  $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
  $items=$combo.FindAll([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::ControlTypeProperty),([Windows.Automation.ControlType]::ListItem)))
  foreach($item in $items){if($item.Current.Name -eq $originalStyle){$item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();break}}
  $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
 }
 SelectSection 'generalNavigationButton'
 Set-TestSegmentVisibility $root 'Taskbar' 1 1
 Toggle 'revealOnAppAttentionCheckBox' $true
 Toggle 'revealOnAppOpenCheckBox' ([bool]$settings.RevealOnAppOpen)
 $savedDuration=5
 if($null -ne $settings.AppAttentionRevealSeconds){$savedDuration=[double]$settings.AppAttentionRevealSeconds}
 Toggle 'keepAppAttentionOpenCheckBox' $false
 Duration $savedDuration
 Toggle 'keepAppAttentionOpenCheckBox' ([bool]$settings.KeepAppAttentionOpenUntilAccepted)
 Toggle 'revealOnAppAttentionCheckBox' ([bool]$settings.RevealOnAppAttention)
 $savedNormal=[int][bool]$settings.DynamicAppListLayout.ShowOnHover
 Set-TestSegmentProfile $root 'Taskbar' $settings.DynamicAppListLayout $savedNormal (Get-TestMaximizedVisibility $settings.DynamicAppListLayout $savedNormal)
 Toggle 'fillMaximisedCheckBox' ([bool]$settings.FillOnMaximise)
 WaitForLiveSettings
 [AttentionProbe]::SetCursorPos($mouse.X,$mouse.Y)|Out-Null
 [AttentionProbe]::DeleteObject($region)|Out-Null
 $root.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState($originalWindowState)
}
