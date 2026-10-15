$ErrorActionPreference='Stop'
$prototype=Join-Path $PSScriptRoot 'Test-TaskbarPinPrototype.ps1'
$running=Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object {
 $_.CommandLine -and $_.CommandLine.IndexOf($prototype,[StringComparison]::OrdinalIgnoreCase) -ge 0
}
foreach ($process in $running) {
 # Only the standalone recorder is stopped, never RoundedTB or Explorer.
 Stop-Process -Id $process.ProcessId -Force
 Write-Output "Stopped standalone native test controller $($process.ProcessId)."
}
Start-Sleep -Milliseconds 800
