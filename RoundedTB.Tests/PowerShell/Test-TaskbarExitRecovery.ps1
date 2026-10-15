$ErrorActionPreference='Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ExitRecoveryProbe {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public uint Size; public Rect Monitor,Work; public uint Flags; }
 [StructLayout(LayoutKind.Sequential)] public struct Appbar { public uint Size; public IntPtr Window; public uint Callback,Edge; public Rect Bounds; public IntPtr Param; }
 [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint message,ref Appbar data);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] public static extern IntPtr FindWindow(string c,string n);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 [DllImport("user32.dll",EntryPoint="GetWindowLongW")] public static extern uint GetWindowLong(IntPtr h,int index);
 [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
 [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr h,ref MonitorInfo info);
 [DllImport("user32.dll")] public static extern int GetWindowRgn(IntPtr h,IntPtr r);
 [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int a,int b,int c,int d);
 [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr r);
}
'@
$exe=Join-Path $env:LOCALAPPDATA 'Programs\RoundedTB\RoundedTB.exe'
$prior=[ExitRecoveryProbe]::SetThreadDpiAwarenessContext([IntPtr](-4))
$region=[ExitRecoveryProbe]::CreateRectRgn(0,0,0,0)
function Start-App {
 Start-Process $exe
 $timer=[Diagnostics.Stopwatch]::StartNew()
 do {
  Start-Sleep -Milliseconds 100
  $p=Get-Process RoundedTB -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
  $helper=Get-Process 'RoundedTB.Recovery' -ErrorAction SilentlyContinue
 } while ((!$p -or !$p.MainWindowHandle -or !$helper) -and $timer.Elapsed.TotalSeconds -lt 15)
 if(!$p -or !$p.MainWindowHandle -or !$helper){throw 'App and recovery helper did not start.'}
 Start-Sleep -Seconds 2
 return $p
}
function Verify-Restored($p,$description) {
 if(!$p.WaitForExit(10000)){throw "$description did not exit."}
 Get-Process 'RoundedTB.Recovery' -ErrorAction SilentlyContinue | Wait-Process -Timeout 15
 $bar=[ExitRecoveryProbe]::FindWindow('Shell_TrayWnd',$null)
 $appbar=New-Object ExitRecoveryProbe+Appbar;$appbar.Size=[Runtime.InteropServices.Marshal]::SizeOf($appbar);$appbar.Window=$bar
 if(([ExitRecoveryProbe]::SHAppBarMessage(4,[ref]$appbar).ToUInt64() -band 1) -ne 0){throw "$description left native auto-hide enabled."}
 if([ExitRecoveryProbe]::GetWindowRgn($bar,$region) -ne 0){throw "$description left a custom taskbar region."}
 if(([ExitRecoveryProbe]::GetWindowLong($bar,-20) -band 0x80020) -ne 0){throw "$description left layered/transparent taskbar styles."}
 $info=New-Object ExitRecoveryProbe+MonitorInfo
 $info.Size=[Runtime.InteropServices.Marshal]::SizeOf($info)
 [ExitRecoveryProbe]::GetMonitorInfo([ExitRecoveryProbe]::MonitorFromWindow($bar,2),[ref]$info)|Out-Null
 if($info.Work.Bottom -ge $info.Monitor.Bottom){throw "$description did not restore the native work area reservation."}
 Write-Output "$description restored taskbar regions, styles, and work area."
}
try {
 $p=Start-App
 Stop-Process -Id $p.Id -Force
 Verify-Restored $p 'PID kill'
 $p=Start-App
 $window=$p.MainWindowHandle
 # A caption-button close carries coordinates and must only hide settings.
 [ExitRecoveryProbe]::PostMessage($window,0x112,[IntPtr]0xf060,[IntPtr]0x00010001)|Out-Null
 Start-Sleep -Milliseconds 500
 $p.Refresh()
 if($p.HasExited){throw 'Settings X unexpectedly exited RoundedTB.'}
 Write-Output 'Settings caption close kept RoundedTB running.'
 # Explorer taskbar Close sends SC_CLOSE without caption coordinates.
 [ExitRecoveryProbe]::PostMessage($window,0x112,[IntPtr]0xf060,[IntPtr]::Zero)|Out-Null
 Start-Sleep -Milliseconds 500
 $p.Refresh()
 if($p.HasExited){throw 'Taskbar Close unexpectedly exited RoundedTB.'}
 Write-Output 'Taskbar Close kept RoundedTB running.'
} finally {
 Start-Process $exe
 [ExitRecoveryProbe]::DeleteObject($region)|Out-Null
 [ExitRecoveryProbe]::SetThreadDpiAwarenessContext($prior)|Out-Null
}
