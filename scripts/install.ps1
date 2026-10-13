param(
    [string]$Source = (Join-Path $PSScriptRoot '..\artifacts\RoundedTB'),
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\RoundedTB'),
    [switch]$EnableStartup,
    [switch]$NoLaunch,
    [switch]$ConfigureOnly,
    [switch]$StopOnly
)

$ErrorActionPreference = 'Stop'
$Source = (Resolve-Path $Source).Path
$Destination = [IO.Path]::GetFullPath($Destination)
if (!(Test-Path (Join-Path $Source 'RoundedTB.exe'))) {
    throw 'Publish the Windows build before installing.'
}
if (!$ConfigureOnly -and !$StopOnly -and $Source.TrimEnd('\') -eq $Destination.TrimEnd('\')) {
    throw 'The published source and install destination must be different.'
}
$legacyDirectory = Join-Path $env:USERPROFILE 'RoundedTB'
$startupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'RoundedTB.lnk'
$runPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runEntry = (Get-ItemProperty -Path $runPath -ErrorAction SilentlyContinue).RoundedTB
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$taskName = 'RoundedTB-' + $sid
$scheduler = New-Object -ComObject Schedule.Service
$scheduler.Connect()
$folder = $scheduler.GetFolder('\')
$existingTask = $null
try { $existingTask = $folder.GetTask($taskName) } catch {
    if ($_.Exception.HResult -ne -2147024894) { throw }
}
$startAtLogin = $EnableStartup -or (Test-Path $startupLink) -or !!$runEntry -or ($existingTask -and $existingTask.Enabled)
Get-Process RoundedTB -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -eq (Join-Path $Destination 'RoundedTB.exe') -or
    $_.Path -eq (Join-Path $legacyDirectory 'RoundedTB.exe')
} | Stop-Process -Force
# The independent helper restores Explorer after Stop-Process, then releases
# the installed binaries before they are replaced.
Get-Process 'RoundedTB.Recovery' -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -eq (Join-Path $Destination 'RoundedTB.Recovery.exe') -or
    $_.Path -eq (Join-Path $legacyDirectory 'RoundedTB.Recovery.exe')
} | Wait-Process -Timeout 15
if ($StopOnly) { return }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
if (!$ConfigureOnly) { Copy-Item (Join-Path $Source '*') $Destination -Recurse -Force }
$exe = Join-Path $Destination 'RoundedTB.exe'
$shell = New-Object -ComObject WScript.Shell
$menuLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'RoundedTB.lnk'
foreach ($linkPath in @($menuLink)) {
    $link = $shell.CreateShortcut($linkPath)
    $link.TargetPath = $exe
    $link.WorkingDirectory = $Destination
    $link.IconLocation = $exe
    $link.Save()
}
if ($startAtLogin) {
    $definition = $scheduler.NewTask(0)
    $definition.RegistrationInfo.Description = 'Start RoundedTB when this user signs in.'
    $definition.Principal.UserId = $sid
    $definition.Principal.LogonType = 3
    $definition.Principal.RunLevel = 0
    $definition.Settings.DisallowStartIfOnBatteries = $false
    $definition.Settings.StopIfGoingOnBatteries = $false
    $definition.Settings.ExecutionTimeLimit = 'PT0S'
    $definition.Settings.MultipleInstances = 2
    $trigger = $definition.Triggers.Create(9)
    $trigger.UserId = $sid
    $trigger.Delay = 'PT0S'
    $action = $definition.Actions.Create(0)
    $action.Path = $exe
    $action.Arguments = '--startup'
    $action.WorkingDirectory = $Destination
    $folder.RegisterTaskDefinition($taskName, $definition, 6, $sid, $null, 3) | Out-Null
    Remove-ItemProperty -Path $runPath -Name 'RoundedTB' -ErrorAction SilentlyContinue
    if (Test-Path $startupLink) { Remove-Item $startupLink }

}
if (!$NoLaunch) { Start-Process -FilePath $exe -WorkingDirectory $Destination }
[pscustomobject]@{ InstalledAt = $Destination; StartupEnabled = $startAtLogin; Executable = $exe }
