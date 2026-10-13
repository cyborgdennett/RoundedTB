param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $Destination 'RoundedTB.exe'
Get-Process RoundedTB -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Stop-Process -Force
Get-Process 'RoundedTB.Recovery' -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -eq (Join-Path $Destination 'RoundedTB.Recovery.exe')
} | Wait-Process -Timeout 15
$scheduler = New-Object -ComObject Schedule.Service
$scheduler.Connect()
$folder = $scheduler.GetFolder('\')
$name = 'RoundedTB-' + [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
try { $task = $folder.GetTask($name) } catch {
    if ($_.Exception.HResult -ne -2147024894) { throw }
    $task = $null
}
if ($task -and $task.Definition.Actions.Item(1).Path -eq $exe) { $folder.DeleteTask($name, 0) }
$runPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$run = (Get-ItemProperty $runPath -ErrorAction SilentlyContinue).RoundedTB
if ($run -and $run.Contains($exe)) { Remove-ItemProperty $runPath -Name RoundedTB }
$linkPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'RoundedTB.lnk'
if (Test-Path $linkPath) {
    $shell = New-Object -ComObject WScript.Shell
    if ($shell.CreateShortcut($linkPath).TargetPath -eq $exe) { Remove-Item $linkPath }
}
# Preferences and logs are retained for reinstalling.
