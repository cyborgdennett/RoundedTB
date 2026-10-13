param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System.Runtime.InteropServices;
using System.Text;
public static class UninstallPathProbe {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint GetLongPathName(string path, StringBuilder result, uint capacity);
}
'@
# Setup and Windows can pass different long/8.3 names for the same directory.
$directory = New-Object Text.StringBuilder 32768
$length = [UninstallPathProbe]::GetLongPathName($Destination, $directory, $directory.Capacity)
if (!$length -or $length -ge $directory.Capacity) { throw "Cannot resolve install directory: $Destination" }
$Destination = $directory.ToString()
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
if ($task) {
    $taskPath = $task.Definition.Actions.Item(1).Path
    $resolved = New-Object Text.StringBuilder 32768
    $length = [UninstallPathProbe]::GetLongPathName($taskPath, $resolved, $resolved.Capacity)
    if ($length -and $length -lt $resolved.Capacity) { $taskPath = $resolved.ToString() }
    if ($taskPath -eq $exe) { $folder.DeleteTask($name, 0) }
}
$runPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$run = (Get-ItemProperty $runPath -ErrorAction SilentlyContinue).RoundedTB
if ($run -and $run.Contains($exe)) { Remove-ItemProperty $runPath -Name RoundedTB }
$linkPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'RoundedTB.lnk'
if (Test-Path $linkPath) {
    $shell = New-Object -ComObject WScript.Shell
    if ($shell.CreateShortcut($linkPath).TargetPath -eq $exe) { Remove-Item $linkPath }
}
# Preferences and logs are retained for reinstalling.
