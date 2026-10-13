param([switch]$Experimental, [switch]$AlwaysShow, [switch]$LiveDuration,
    [switch]$AttentionButton, [switch]$KeepOpen, [ValidateRange(0,2)][int]$TestStyle=0)
$ErrorActionPreference = 'Stop'
if($AlwaysShow -and !$Experimental){throw '-AlwaysShow requires -Experimental.'}
if($AttentionButton -and $Experimental){throw 'Select attention-button or experimental checks.'}
if(($KeepOpen -or $TestStyle -ne 0) -and !$AttentionButton){throw '-KeepOpen and -TestStyle require -AttentionButton.'}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Start-Process (Join-Path $env:LOCALAPPDATA 'Programs\RoundedTB\RoundedTB.exe')
Start-Sleep -Seconds 1
$process = Get-Process RoundedTB | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (!$process) { throw 'RoundedTB settings did not open.' }
$root = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
$bounds = $root.Current.BoundingRectangle
$transform = $root.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern)
$results = @()
$config = Join-Path $env:LOCALAPPDATA 'rtb.json'
$originalPreferences = Get-Content $config -Raw | ConvertFrom-Json | ConvertTo-Json -Depth 20 -Compress
try {
    $transform.Resize(1200, 720)
    Start-Sleep -Milliseconds 300
    $scripts = if ($Experimental) { @('Test-ExperimentalInstantReveal.ps1') }
    elseif ($AttentionButton) { @('Test-AppAttentionReveal.ps1') } else {
        @('Test-SettingsNavigation.ps1', 'Test-WindowsApiDiagnostics.ps1', 'Test-WindowModeVisibility.ps1', 'Test-SegmentAutoHide.ps1', 'Test-AppAttentionReveal.ps1')
    }
    foreach ($script in $scripts) {
        $picker = $root.FindFirst([Windows.Automation.TreeScope]::Descendants,
            (New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty), 'taskbarRectStandIn'))
        $picker.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        try {
            if ($Experimental) { & (Join-Path $PSScriptRoot $script) -AlwaysShow:$AlwaysShow }
            elseif ($AttentionButton) { & (Join-Path $PSScriptRoot $script) -UseRoundedTBButton -KeepOpen:$KeepOpen -TestStyle $TestStyle }
            elseif ($script -eq 'Test-SegmentAutoHide.ps1') { & (Join-Path $PSScriptRoot $script) -LiveDuration:$LiveDuration }
            else { & (Join-Path $PSScriptRoot $script) }
            $results += "$script PASS"
        } catch {
            $results += "$script FAIL: $($_.Exception.Message)"
        }
    }
} finally {
    $transform.Resize($bounds.Width, $bounds.Height)
    Start-Sleep -Milliseconds 600
    $restoredPreferences = Get-Content $config -Raw | ConvertFrom-Json | ConvertTo-Json -Depth 20 -Compress
    if ($restoredPreferences -ne $originalPreferences) { $results += 'Preference restoration FAIL: saved settings changed.' }
}
$results | Write-Output
if ($results | Where-Object { $_ -like '* FAIL:*' }) { throw 'One or more native audit checks failed.' }
