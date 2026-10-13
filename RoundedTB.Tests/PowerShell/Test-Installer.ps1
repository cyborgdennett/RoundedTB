param([string]$Installer = (Join-Path $PSScriptRoot '..\..\artifacts\installer\RoundedTB-Setup-1.0.0-win-x64.exe'))
$ErrorActionPreference = 'Stop'
$Installer = (Resolve-Path $Installer).Path
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8DBF5D1-CF65-4736-9556-711CBE407756}_is1'
if (Test-Path $uninstallKey) { throw 'Run this isolated installer check before installing the product with Setup, or in a clean test VM.' }
$destination = Join-Path $env:TEMP ('RoundedTB Installer Test ' + [guid]::NewGuid())
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'RoundedTB.lnk'
$shortcutBytes = if (Test-Path $shortcut) { [IO.File]::ReadAllBytes($shortcut) } else { $null }
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$taskName = 'RoundedTB-' + $sid
$service = New-Object -ComObject Schedule.Service
$service.Connect()
$folder = $service.GetFolder('\')
try { $originalTask = $folder.GetTask($taskName); $originalXml = $originalTask.Xml } catch {
    if ($_.Exception.HResult -ne -2147024894) { throw }
    $originalXml = $null
}
function Run-Setup([string]$Path, [string]$Arguments) {
    $p = Start-Process $Path -ArgumentList $Arguments -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Setup returned $($p.ExitCode): $Path" }
}
try {
    # Windows may refuse to execute installers directly from a WSL UNC share.
    $localInstaller = Join-Path $env:TEMP ('RoundedTB-SetupTest-' + [guid]::NewGuid() + '.exe')
    Copy-Item $Installer $localInstaller
    foreach ($attempt in 1..2) {
        Run-Setup $localInstaller "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS=startup /DIR=`"$destination`" /LOG=`"$destination-install-$attempt.log`""
        foreach ($file in @('RoundedTB.exe', 'RoundedTB.Recovery.exe', 'unins000.exe', 'uninstall.ps1')) {
            if (!(Test-Path (Join-Path $destination $file))) { throw "Missing installed file: $file" }
        }
        if (!(Test-Path $uninstallKey)) { throw 'Missing Apps uninstall entry.' }
        $task = $folder.GetTask($taskName)
        if ($task.Definition.Actions.Item(1).Path -ne (Join-Path $destination 'RoundedTB.exe')) { throw 'Startup points to the wrong executable.' }
        Write-Output "Installation/upgrade $attempt passed."
    }
    Run-Setup (Join-Path $destination 'unins000.exe') '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
    if (Test-Path (Join-Path $destination 'RoundedTB.exe')) { throw 'Uninstall left the app executable.' }
    if (Test-Path $uninstallKey) { throw 'Uninstall left the Apps entry.' }
    try { $null = $folder.GetTask($taskName); throw 'Uninstall left the startup task.' } catch {
        if ($_.Exception.HResult -ne -2147024894) { throw }
    }
    Write-Output 'Uninstall removed application, Apps entry, and startup task.'
} finally {
    if (Test-Path (Join-Path $destination 'unins000.exe')) {
        Run-Setup (Join-Path $destination 'unins000.exe') '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
    }
    if ($originalXml) { $folder.RegisterTask($taskName, $originalXml, 6, $sid, $null, 3) | Out-Null }
    if ($shortcutBytes) { [IO.File]::WriteAllBytes($shortcut, $shortcutBytes) }
    if ($localInstaller -and (Test-Path $localInstaller)) { Remove-Item $localInstaller }
}
