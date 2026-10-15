$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SessionWindowStateProbe {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetProp(IntPtr h,string n);
 [DllImport("user32.dll",EntryPoint="GetWindowLongW")] public static extern uint GetWindowLong(IntPtr h,int n);
}
'@
$path = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'RoundedTB\Diagnostics') -Filter '*-8.json' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
$data = Get-Content $path.FullName -Raw | ConvertFrom-Json
foreach ($window in $data.Samples[0].Windows | Where-Object { $_.State -eq 3 }) {
 $handle = [IntPtr]$window.Handle
 [pscustomobject]@{Handle=$window.Handle;Style=('{0:X8}' -f [SessionWindowStateProbe]::GetWindowLong($handle,-16));NonRude=[SessionWindowStateProbe]::GetProp($handle,'NonRudeHWND').ToInt64();Owner=[SessionWindowStateProbe]::GetProp($handle,'RoundedTB.NonRudeOwner').ToInt64()}
}
