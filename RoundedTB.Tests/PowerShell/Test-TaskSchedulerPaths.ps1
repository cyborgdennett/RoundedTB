$ErrorActionPreference = 'Stop'
$service = New-Object -ComObject Schedule.Service
$service.Connect()
$folder = $service.GetFolder('\')
$name = 'RoundedTB-PathTest-' + [guid]::NewGuid()
$exe = Join-Path $env:TEMP 'RoundedTB Path Test\RoundedTB.exe'
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
try {
    $definition = $service.NewTask(0)
    $definition.Principal.UserId = $sid
    $definition.Principal.LogonType = 3
    $definition.Settings.Enabled = $false
    $action = $definition.Actions.Create(0)
    $action.Path = $exe
    $folder.RegisterTaskDefinition($name, $definition, 6, $sid, $null, 3) | Out-Null
    $stored = $folder.GetTask($name).Definition.Actions.Item(1).Path
    Write-Output "Requested path: $exe"
    Write-Output "Stored path: $stored"
    if ($stored.Trim('"') -ne $exe) { throw 'Task Scheduler changed the executable path.' }
} finally {
    try { $folder.DeleteTask($name, 0) } catch {
        if ($_.Exception.HResult -ne -2147024894) { throw }
    }
}
