param([switch]$UseCachedSymbols,[string]$AppDirectory=(Join-Path $env:LOCALAPPDATA 'Programs\RoundedTB'))
$ErrorActionPreference='Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ExplorerRestartProbe {
 [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls,string name);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int L,T,R,B;}
 [StructLayout(LayoutKind.Sequential)] public struct Appbar {public uint Size;public IntPtr Window;public uint Callback,Edge;public Rect Bounds;public IntPtr Param;}
 [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string name);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window,out Rect rect);
 [DllImport("user32.dll")] public static extern int GetWindowRgn(IntPtr window,IntPtr region);
 [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int l,int t,int r,int b);
 [DllImport("gdi32.dll")] public static extern bool PtInRegion(IntPtr region,int x,int y);
 [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr region);
 [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint message,ref Appbar data);
 public static uint State(){var d=new Appbar{Size=(uint)Marshal.SizeOf(typeof(Appbar)),Window=FindWindow("Shell_TrayWnd",null)};return (uint)SHAppBarMessage(4,ref d).ToUInt64();}
}
'@
$settings=Get-Content (Join-Path $env:LOCALAPPDATA 'rtb.json') -Raw|ConvertFrom-Json
if($settings.IsWindowsDefault -or !$settings.ExpandMaximizedWorkArea){throw 'Enable RoundedTB and experimental work-area reclamation before this test.'}
$p=Get-Process RoundedTB -ErrorAction Stop|Select-Object -First 1
$old=[ExplorerRestartProbe]::FindWindow('Shell_TrayWnd',$null)
[uint32]$owner=0
[void][ExplorerRestartProbe]::GetWindowThreadProcessId($old,[ref]$owner)
if(!$owner -or (Get-Process -Id $owner).ProcessName -ne 'explorer'){throw 'Cannot identify primary taskbar Explorer process.'}
$packaged=Join-Path $AppDirectory 'Native\symbols'
$backup=$packaged+'.restart-test-'+[guid]::NewGuid().ToString('N')
$log=Join-Path $env:LOCALAPPDATA 'rtb.log'
$offset=(Get-Item $log).Length
try {
 if($UseCachedSymbols){
  $cache=Join-Path $env:LOCALAPPDATA 'RoundedTB\NativeSymbols\Resolved-v1'
  if(!(Test-Path $cache) -or @(Get-ChildItem $cache -Filter '*.json').Count -lt 2){throw 'Resolve both Windows modules before testing cached restart.'}
  if(Test-Path $packaged){Move-Item $packaged $backup}
 }
 Write-Output "Restarting the primary Explorer process $owner; RoundedTB process $($p.Id) must survive."
 Stop-Process -Id $owner -Force
 $watch=[Diagnostics.Stopwatch]::StartNew();$started=$false;$tail=''
 do {
  Start-Sleep -Milliseconds 200
  $p.Refresh();if($p.HasExited){throw 'RoundedTB exited during Explorer restart.'}
  $bar=[ExplorerRestartProbe]::FindWindow('Shell_TrayWnd',$null)
  if($bar -eq [IntPtr]::Zero -and !$started -and $watch.Elapsed.TotalSeconds -gt 5){Start-Process explorer.exe;$started=$true}
  $stream=[IO.File]::Open($log,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
  try {$stream.Position=$offset;$reader=New-Object IO.StreamReader $stream;$tail=$reader.ReadToEnd()}finally{$stream.Dispose()}
 } while(($bar -eq [IntPtr]::Zero -or $tail -notmatch 'Native taskbar pin active:') -and $watch.Elapsed.TotalSeconds -lt 35)
 if($bar -eq [IntPtr]::Zero -or $tail -notmatch 'Native taskbar pin active:'){throw "Native taskbar helper did not reattach after Explorer restart.`n$tail"}
 [uint32]$nextOwner=0
 [void][ExplorerRestartProbe]::GetWindowThreadProcessId($bar,[ref]$nextOwner)
 if($nextOwner -eq $owner){throw 'The primary Explorer process was not replaced.'}
 if(([ExplorerRestartProbe]::State() -band 1) -eq 0){throw 'Native space reclamation did not resume.'}
 if($UseCachedSymbols -and $tail -match 'Resolving exact Windows'){throw 'Cached restart unexpectedly requested symbol downloads.'}
 $mapping=[IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting('Local\RoundedTB.TaskbarPin.'+$p.Id)
 try {
  $view=$mapping.CreateViewAccessor()
  try {if($view.ReadInt32(68) -ne 1 -or $view.ReadInt32(72) -ne 1){throw 'Native helper mapping is not enabled and ready.'}}finally{$view.Dispose()}
 } finally {$mapping.Dispose()}
 Write-Output "Explorer changed from PID $owner to $nextOwner. RoundedTB survived and native hooks reattached in $([int]$watch.Elapsed.TotalMilliseconds) ms."
 if($UseCachedSymbols){Write-Output 'Verified local symbols reused without downloads.'}
 if($settings.ShowTray -and !$settings.DynamicTrayLayout.ShowOnHover -and (!$settings.DynamicTrayLayout.MaximizedVisibility)){
  Start-Process (Join-Path $AppDirectory 'RoundedTB.exe')
  $trayWatch=[Diagnostics.Stopwatch]::StartNew();$visible=$false
  do {
   Start-Sleep -Milliseconds 100
   $tray=[ExplorerRestartProbe]::FindWindowEx($bar,[IntPtr]::Zero,'TrayNotifyWnd',$null)
   $barRect=New-Object ExplorerRestartProbe+Rect;$trayRect=New-Object ExplorerRestartProbe+Rect
   [void][ExplorerRestartProbe]::GetWindowRect($bar,[ref]$barRect)
   [void][ExplorerRestartProbe]::GetWindowRect($tray,[ref]$trayRect)
   $region=[ExplorerRestartProbe]::CreateRectRgn(0,0,0,0)
   try {
    $kind=[ExplorerRestartProbe]::GetWindowRgn($bar,$region)
    $visible=$tray -ne [IntPtr]::Zero -and $trayRect.R -gt $trayRect.L -and ($kind -eq 0 -or [ExplorerRestartProbe]::PtInRegion($region,[int](($trayRect.L+$trayRect.R)/2)-$barRect.L,[int](($trayRect.T+$trayRect.B)/2)-$barRect.T))
   } finally {[void][ExplorerRestartProbe]::DeleteObject($region)}
  } while(!$visible -and $trayWatch.Elapsed.TotalSeconds -lt 8)
  if(!$visible){throw 'The clock/notification area remained clipped out after Explorer restart.'}
  Write-Output 'Always-visible clock/notification area recovered and is included in the taskbar region.'
 }
} finally {
 if(Test-Path $backup){Move-Item $backup $packaged}
 if([ExplorerRestartProbe]::FindWindow('Shell_TrayWnd',$null) -eq [IntPtr]::Zero){Start-Process explorer.exe}
}
