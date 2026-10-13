$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Start-Process (Join-Path $env:LOCALAPPDATA 'Programs\RoundedTB\RoundedTB.exe')
$timer=[Diagnostics.Stopwatch]::StartNew()
do {
 Start-Sleep -Milliseconds 200
 $p=Get-Process RoundedTB -ErrorAction SilentlyContinue|Where-Object {$_.MainWindowHandle -ne 0}|Select-Object -First 1
} while(!$p -and $timer.Elapsed.TotalSeconds -lt 20)
if(!$p){throw 'RoundedTB settings did not open.'}
$root=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
function Control($id){$root.FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty),$id))}
$scroll=(Control 'settingsScrollViewer').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
try {
 foreach($id in @('visibilityAlwaysButton','cornerRadiusInput','revealOnAppAttentionCheckBox','expandWorkAreaCheckBox','StartupCheckBox')){
  if(!(Control $id)){throw "$id is missing from the continuous page."}
 }
 (Control 'generalNavigationButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
 foreach($id in @('taskbarRectStandIn','trayRectStandIn','widgetsRectStandIn','taskbarRectStandIn','trayRectStandIn','taskbarRectStandIn')) {
  $area=Control $id
  if($area.Current.IsOffscreen){continue}
  $area.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 150
  if($p.HasExited){throw 'RoundedTB exited while switching preview areas.'}
 }
 if(Control 'applyButton'){throw 'The obsolete Apply button is still exposed.'}
 if(!(Control 'restoreWindowsButton')){throw 'The master taskbar control is missing.'}
 if((Control 'StartupCheckBox').Current.Name -ne 'Launch RoundedTB at Windows sign-in'){throw 'Startup label was not updated.'}
 foreach($id in @('generalNavigationButton','appearanceNavigationButton','notificationsNavigationButton','windowsNavigationButton','advancedNavigationButton')) {
  if(!(Control $id)){throw "Missing section link $id"}
 }
 (Control 'advancedNavigationButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
 Start-Sleep -Milliseconds 300
 if($scroll.Current.VerticalScrollPercent -le 0){throw 'Advanced did not scroll the continuous page.'}
 $before=$scroll.Current.VerticalScrollPercent
 $scroll.Scroll([Windows.Automation.ScrollAmount]::NoAmount,[Windows.Automation.ScrollAmount]::SmallDecrement)
 Start-Sleep -Milliseconds 100
 if($scroll.Current.VerticalScrollPercent -ge $before){throw 'The page did not scroll back toward the preceding settings.'}
 (Control 'appearanceNavigationButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
 $duration=(Control 'cornerRadiusInput').GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern)
 $originalDuration=$duration.Current.Value
 try {
  $testDuration=if($originalDuration -eq 2){3}else{2}
  $duration.SetValue($testDuration)
  $timer=[Diagnostics.Stopwatch]::StartNew()
  do {
   Start-Sleep -Milliseconds 100
   $saved=Get-Content (Join-Path $env:LOCALAPPDATA 'rtb.json') -Raw|ConvertFrom-Json
  } while($saved.DynamicAppListLayout.CornerRadius -ne $testDuration -and $timer.Elapsed.TotalSeconds -lt 5)
  if($saved.DynamicAppListLayout.CornerRadius -ne $testDuration){throw 'Corner radius did not save automatically.'}
 } finally {$duration.SetValue($originalDuration)}
 (Control 'advancedNavigationButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
 $master=Control 'restoreWindowsButton' 
 if($master.Current.IsOffscreen){throw 'The master control scrolled out of view.'}
 $originalName=$master.Current.Name
 $invoke=$master.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
 $invoke.Invoke()
 $timer=[Diagnostics.Stopwatch]::StartNew()
 do {Start-Sleep -Milliseconds 100} while(($master.Current.Name -eq $originalName -or !$master.Current.IsEnabled) -and $timer.Elapsed.TotalSeconds -lt 15)
 if($master.Current.Name -eq $originalName){throw 'The master control did not switch modes.'}
 $invoke.Invoke()
 $timer.Restart()
 do {Start-Sleep -Milliseconds 100} while(($master.Current.Name -ne $originalName -or !$master.Current.IsEnabled) -and $timer.Elapsed.TotalSeconds -lt 15)
 if($master.Current.Name -ne $originalName){throw 'The master control did not restore the original mode.'}
 Write-Output 'Master control stays visible, switches modes, and returns to the original mode. No Apply button. All settings share one scroll. Section links and ordinary scrolling work. Startup label verified.'
} finally {
 (Control 'generalNavigationButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}
