$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$exe = Join-Path $env:LOCALAPPDATA 'Programs\RoundedTB\RoundedTB.exe'
$config = Join-Path $env:LOCALAPPDATA 'rtb.json'
$before = [IO.File]::ReadAllText($config)
Start-Process $exe
$deadline = [Diagnostics.Stopwatch]::StartNew()
$root = $null
while ($deadline.Elapsed.TotalSeconds -lt 30) {
    $process = Get-Process RoundedTB -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($process -and $process.MainWindowHandle -ne 0) {
        $root = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        break
    }
    Start-Sleep -Milliseconds 100
}
if (!$root) { throw 'Configuration window did not open.' }
function Control([string]$id) {
    $condition = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty), $id
    return $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}
(Control 'advancedNavigationButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 200
$button = Control 'testWindowsApisButton'
$deadline.Restart()
while (!$button.Current.IsEnabled -and $deadline.Elapsed.TotalSeconds -lt 60) { Start-Sleep -Milliseconds 100 }
if (!$button.Current.IsEnabled) { throw 'Diagnostics button unavailable.' }
$button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
$deadline.Restart()
$report = ''
while ($deadline.Elapsed.TotalSeconds -lt 60) {
    Start-Sleep -Milliseconds 200
    $text = Control 'diagnosticsReport'
    if ($text) { $report = $text.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value }
    if ($report -match 'Report saved to' -and $button.Current.IsEnabled) { break }
}
if ($report -notmatch 'Report saved to') { throw "Diagnostics did not complete: $report" }
if ($report -notmatch '\[PASS\].*Taskbar.*opacity') { throw "Taskbar opacity probe did not pass: $report" }
if ($report -notmatch '\[PASS\].*SPI_SETWORKAREA') { throw "Work-area probe did not pass: $report" }
if ($report -match '\[(FAIL|ACCESS DENIED)\].*Taskbar') { throw "Taskbar access failed: $report" }
if (!$button.Current.IsEnabled -or !(Control 'restoreWindowsButton').Current.IsEnabled) { throw 'Settings did not resume.' }
if ([IO.File]::ReadAllText($config) -ne $before) { throw 'Diagnostics changed saved preferences.' }
Write-Output $report
Write-Output 'Saved preferences preserved; settings controls resumed.'
