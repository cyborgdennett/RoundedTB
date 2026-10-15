param([ValidateRange(5,900)][int]$Seconds=15,[switch]$CompileOnly,[string]$SymbolsPath,[switch]$CaptureTaskbar,[switch]$WaitForUnlock)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path (Join-Path $repo 'native\TaskbarPin\PrototypeController.cs')
if ($CompileOnly) { Write-Output 'Native controller compiled; no desktop changes.'; return }
$settings=Get-Content (Join-Path $env:LOCALAPPDATA 'rtb.json') -Raw | ConvertFrom-Json
if ($settings.IsWindowsDefault -or !$settings.ExpandMaximizedWorkArea) { throw 'Requires enabled RoundedTB with experimental space reclamation selected.' }
if (!$SymbolsPath) { $SymbolsPath=Join-Path $repo 'artifacts\native\symbols.json' }
$symbols=Get-Content $SymbolsPath -Raw | ConvertFrom-Json
if ($symbols.Version -ne 1) { throw 'Unsupported symbol descriptor.' }
$taskbar=$symbols.Modules | Where-Object {$_.Name -eq 'taskbar.dll'}
$view=$symbols.Modules | Where-Object {$_.Name -eq 'Taskbar.View.dll'}
if (!$taskbar -or !$view) { throw 'Both exact-build symbol modules are required.' }
$config=New-Object TaskbarPinController+Configuration
$config.TaskbarTimestamp=$taskbar.Timestamp; $config.TaskbarImageSize=$taskbar.ImageSize
$config.ViewTimestamp=$view.Timestamp; $config.ViewImageSize=$view.ImageSize
$config.Hide=$taskbar.Symbols.Hide.Rva; $config.SecondaryHide=$taskbar.Symbols.SecondaryAutoHide.Rva
$config.ShouldExpand=$view.Symbols.ShouldExpand.Rva; $config.UpdateExpanded=$view.Symbols.UpdateIsExpanded.Rva
$config.TaskbarModule=$taskbar.Name; $config.ViewModule=$view.Name
$source=Join-Path $repo 'artifacts\native-pin\RoundedTB.TaskbarPin.dll'
$hash=(Get-FileHash $source -Algorithm SHA256).Hash.Substring(0,12)
$directory=Join-Path $env:LOCALAPPDATA 'RoundedTB\NativePrototype'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$dll=Join-Path $directory "RoundedTB.TaskbarPin-$hash.dll"
if (!(Test-Path $dll)) { Copy-Item $source $dll }
$controller=$null
$samples=New-Object 'System.Collections.Generic.List[object]'
$iconChecks=New-Object 'System.Collections.Generic.List[object]'
$screenshots=New-Object 'System.Collections.Generic.List[string]'
$priorDpi=[TaskbarPinController]::SetThreadDpiAwarenessContext([IntPtr](-4))
$failure=$null; $counters=$null; $nativeAfter=$null
$windows=[TaskbarPinController]::CaptureWindowHandles()
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
if ($CaptureTaskbar) { Add-Type -AssemblyName System.Drawing }
function Inspect-Taskbar([string]$stage) {
 try {
  $root=[Windows.Automation.AutomationElement]::FromHandle($controller.Taskbar)
  $condition=New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::NameProperty),'Start'
  $start=$root.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
  if ($start) {
   $rect=$start.Current.BoundingRectangle
   $iconChecks.Add([pscustomobject]@{Stage=$stage;Tick=[TaskbarPinController]::GetTickCount64();StartOffscreen=$start.Current.IsOffscreen;StartBounds=[pscustomobject]@{X=$rect.X;Y=$rect.Y;Width=$rect.Width;Height=$rect.Height}})
  } else { $iconChecks.Add([pscustomobject]@{Stage=$stage;Result='Start element unavailable'}) }
 } catch { $iconChecks.Add([pscustomobject]@{Stage=$stage;Error=$_.Exception.Message}) }
 if ($CaptureTaskbar) {
  $monitor=New-Object TaskbarPinController+MonitorInfo; $monitor.Size=40
  [TaskbarPinController]::GetMonitorInfo([TaskbarPinController]::MonitorFromWindow($controller.Taskbar,2),[ref]$monitor)|Out-Null
  $width=$monitor.Bounds.Right-$monitor.Bounds.Left
  $bitmap=New-Object Drawing.Bitmap ($width,64)
  $graphics=[Drawing.Graphics]::FromImage($bitmap)
  try {
   $graphics.CopyFromScreen($monitor.Bounds.Left,($monitor.Bounds.Bottom-64),0,0,$bitmap.Size)
   $image=Join-Path $repo "artifacts\native-pin\taskbar-$stage.png"
   $bitmap.Save($image,[Drawing.Imaging.ImageFormat]::Png)
   $screenshots.Add($image)
  } finally { $graphics.Dispose(); $bitmap.Dispose() }
 }
}
try {
 $controller=New-Object TaskbarPinController ($dll,$config)
 Write-Output "READY: Native taskbar pin active for $Seconds seconds. RoundedTB continues controlling segment visibility. The original native state will be restored afterward."
 $watch=[Diagnostics.Stopwatch]::StartNew()
 $inspected=$false
 $unlockObservedAt=$null
 if ($WaitForUnlock) { Write-Output "WAITING FOR UNLOCK: Recording will continue until 10 seconds after an unlock, with a maximum of $Seconds seconds." }
 while ($watch.Elapsed.TotalSeconds -lt $Seconds) {
  if ($controller.Status -ne 1) { throw "Native controller stopped unexpectedly: status $($controller.Status), error $($controller.Error)." }
  $monitor=New-Object TaskbarPinController+MonitorInfo; $monitor.Size=40
  [TaskbarPinController]::GetMonitorInfo([TaskbarPinController]::MonitorFromWindow($controller.Taskbar,2),[ref]$monitor) | Out-Null
  $bar=New-Object TaskbarPinController+Rect
  [TaskbarPinController]::GetWindowRect($controller.Taskbar,[ref]$bar) | Out-Null
  $samples.Add([pscustomobject]@{Tick=[TaskbarPinController]::GetTickCount64();Milliseconds=$watch.ElapsedMilliseconds;Work=$monitor.Work;Bounds=$monitor.Bounds;Taskbar=$bar;Windows=[TaskbarPinController]::MeasureWindows($windows);HideBlocked=$controller.HideBlocked;ExpandForced=$controller.ExpandForced;LockEvents=$controller.LockEvents;UnlockEvents=$controller.UnlockEvents})
  if ($WaitForUnlock -and $controller.UnlockEvents -gt 0) {
   if ($null -eq $unlockObservedAt) { $unlockObservedAt=$watch.ElapsedMilliseconds; Write-Output 'UNLOCK OBSERVED: Recording ten more seconds before cleanup.' }
   if ($watch.ElapsedMilliseconds-$unlockObservedAt -ge 10000) { break }
  }
  if (!$inspected -and $watch.ElapsedMilliseconds -ge 3000) { Inspect-Taskbar 'active'; $inspected=$true }
  Start-Sleep -Milliseconds 30
 }
 $counters=[pscustomobject]@{HideBlocked=$controller.HideBlocked;ExpandForced=$controller.ExpandForced}
 if ($WaitForUnlock -and $null -eq $unlockObservedAt) { throw 'No unlock was observed before the test timeout; unlock behavior is unverified.' }
 Inspect-Taskbar 'ending'
 Write-Output "Hook counters: hide blocked=$($controller.HideBlocked), expansion forced=$($controller.ExpandForced)."
} catch { $failure=$_.Exception.Message; throw }
finally {
 if ($controller) { $controller.Dispose(); $nativeAfter=[TaskbarPinController]::NativeState($controller.Taskbar) }
 [TaskbarPinController]::SetThreadDpiAwarenessContext($priorDpi) | Out-Null
 $diagnostics=Join-Path $env:LOCALAPPDATA 'RoundedTB\Diagnostics'
 New-Item -ItemType Directory -Path $diagnostics -Force | Out-Null
 $path=Join-Path $diagnostics ('native-pin-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')+'.json')
 [pscustomobject]@{Failure=$failure;Seconds=$Seconds;Counters=$counters;NativeStateAfter=$nativeAfter;IconChecks=$iconChecks.ToArray();Screenshots=$screenshots.ToArray();Samples=$samples.ToArray()} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path -Encoding UTF8
 Write-Output "Native prototype stopped. Geometry saved to $path"
}
