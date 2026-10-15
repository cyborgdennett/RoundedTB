param([ValidateRange(5,300)][int]$Seconds=120,[switch]$CompileOnly,[switch]$TemporarilyRestoreRoundedTB,[switch]$OffTransition,[ValidateSet(0,2)][uint32]$ExpansionFlags=0,[switch]$LockMaximizedWindow,[switch]$HideTimerProbe,[switch]$CancelNativeHideTimer)
$ErrorActionPreference='Stop'
$settingsPath=Join-Path $env:LOCALAPPDATA 'rtb.json'
function Assert-WindowsDefault {
    $settings=Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    if (!$settings.IsWindowsDefault) { throw 'Click Restore Taskbar in RoundedTB first. This diagnostic requires Windows default mode throughout.' }
    if (!(Get-Process RoundedTB -ErrorAction SilentlyContinue)) { throw 'Keep RoundedTB running in Windows default mode during this diagnostic.' }
}
Add-Type @'
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
public static class NativeUnlockProbe {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
 [StructLayout(LayoutKind.Sequential)] public struct Placement { public uint Length,Flags,Show; public Point Min,Max; public Rect Normal; }
 [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public uint Size; public Rect Bounds,Work; public uint Flags; }
 [StructLayout(LayoutKind.Sequential)] public struct Appbar { public uint Size; public IntPtr Window; public uint Callback,Edge; public Rect Bounds; public IntPtr Param; }
 public class Display { public long Handle; public Rect Bounds,Work; }
 public class Window { public long Handle; public uint State,ProcessId; public string Class; public Rect Outer,Client,Frame; public int FrameResult; }
 public class Call { public string Operation; public ulong BeginTick,EndTick; public long Monitor; public uint State,Flags; public bool Success; }
 public class TimerResult { public ulong BeginTick,EndTick; public bool Success; public int LastError; }
 public class Transition {
  public readonly ConcurrentQueue<Call> Writes=new ConcurrentQueue<Call>();
  public volatile bool Completed;
  public volatile string Error;
  private volatile bool cancelled;
  private Thread worker;
  private int ownsWindowLock;
  public Transition(IntPtr bar,uint state,uint flags,IntPtr lockWindow) {
   worker=new Thread(delegate() {
    IntPtr dpi=SetThreadDpiAwarenessContext(new IntPtr(-4));
    try {
     if(lockWindow!=IntPtr.Zero) {
      ulong lockBegin=GetTickCount64(); bool success=LockWindowUpdate(lockWindow);
      Writes.Enqueue(new Call {Operation="LockWindowUpdate",BeginTick=lockBegin,EndTick=GetTickCount64(),Monitor=lockWindow.ToInt64(),Success=success});
      if(!success) throw new InvalidOperationException("Inconclusive: LockWindowUpdate could not acquire the window lock.");
      Interlocked.Exchange(ref ownsWindowLock,1);
     }
     ulong begin=GetTickCount64(); SetState(bar,state);
     Writes.Enqueue(new Call {Operation="NativeAutoHideOff",BeginTick=begin,EndTick=GetTickCount64(),State=state,Success=true});
     foreach(var display in Displays()) {
      if(cancelled) break;
      begin=GetTickCount64(); bool success=SystemParametersInfo(47,0,ref display.Bounds,flags);
      Writes.Enqueue(new Call {Operation="ExpandWorkAreaOnce",BeginTick=begin,EndTick=GetTickCount64(),Monitor=display.Handle,Flags=flags,Success=success});
      if(!success) throw new InvalidOperationException("Single work-area expansion failed.");
     }
    } catch(Exception error) {Error=error.ToString();}
    finally {ReleaseWindowLock(); SetThreadDpiAwarenessContext(dpi); Completed=true;}
   }); worker.IsBackground=true; worker.Start();
  }
  public bool CancelAndJoin() {cancelled=true; return worker.Join(20000);}
  public void ReleaseWindowLock() {
   if(Interlocked.Exchange(ref ownsWindowLock,0)==1) {
    ulong begin=GetTickCount64(); bool success=LockWindowUpdate(IntPtr.Zero);
    Writes.Enqueue(new Call {Operation="UnlockWindowUpdate",BeginTick=begin,EndTick=GetTickCount64(),Success=success});
    if(!success) Error="Inconclusive: LockWindowUpdate release failed.";
   }
  }
 }
 private delegate bool MonitorCallback(IntPtr h,IntPtr dc,ref Rect r,IntPtr data);
 private delegate bool WindowCallback(IntPtr h,IntPtr data);
 [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorCallback cb,IntPtr data);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr h,ref MonitorInfo info);
 [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback cb,IntPtr data);
 [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr h,ref Placement p);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h,out Rect r);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr h,StringBuilder name,int size);
 [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h,out uint processId);
 [DllImport("user32.dll")] private static extern bool LockWindowUpdate(IntPtr h);
 [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h,int a,out Rect r,int size);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c,string n);
 [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
 [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message,ref Appbar data);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action,uint param,ref Rect r,uint flags);
 [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();
 [DllImport("kernel32.dll")] private static extern void SetLastError(uint error);
 [DllImport("user32.dll",SetLastError=true)] private static extern bool KillTimer(IntPtr h,UIntPtr id);
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 public static TimerResult CancelHideTimer(IntPtr bar) {
  ulong begin=GetTickCount64(); SetLastError(0); bool success=KillTimer(bar,new UIntPtr(2)); int error=Marshal.GetLastWin32Error();
  return new TimerResult {BeginTick=begin,EndTick=GetTickCount64(),Success=success,LastError=error};
 }
 public static uint State(IntPtr bar) { var a=new Appbar {Size=(uint)Marshal.SizeOf(typeof(Appbar)),Window=bar}; return (uint)SHAppBarMessage(4,ref a).ToUInt64(); }
 public static void SetState(IntPtr bar,uint state) { var a=new Appbar {Size=(uint)Marshal.SizeOf(typeof(Appbar)),Window=bar,Param=new IntPtr((long)state)}; SHAppBarMessage(10,ref a); }
 public static Display[] Displays() {
  var result=new List<Display>();
  EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,delegate(IntPtr h,IntPtr dc,ref Rect r,IntPtr data) {
   var m=new MonitorInfo {Size=(uint)Marshal.SizeOf(typeof(MonitorInfo))};
   if(GetMonitorInfo(h,ref m)) result.Add(new Display {Handle=h.ToInt64(),Bounds=m.Bounds,Work=m.Work}); return true;
  },IntPtr.Zero); return result.ToArray();
 }
 public static Window[] MaximizedWindows() {
  var result=new List<Window>();
  EnumWindows(delegate(IntPtr h,IntPtr data) {
   var p=new Placement {Length=(uint)Marshal.SizeOf(typeof(Placement))};
   if(IsWindowVisible(h)&&GetWindowPlacement(h,ref p)&&p.Show==3) {
    Rect outer,client,frame; GetWindowRect(h,out outer); GetClientRect(h,out client);
    int status=DwmGetWindowAttribute(h,9,out frame,16);
    var name=new StringBuilder(256); GetClassName(h,name,name.Capacity); uint processId; GetWindowThreadProcessId(h,out processId);
    result.Add(new Window {Handle=h.ToInt64(),State=p.Show,ProcessId=processId,Class=name.ToString(),Outer=outer,Client=client,Frame=frame,FrameResult=status});
   } return true;
  },IntPtr.Zero); return result.ToArray();
 }
 public static bool RestoreWork(Rect area) { return SystemParametersInfo(47,0,ref area,2); }
}
'@
if ($CompileOnly) { Write-Output 'Native diagnostic bindings compiled. No desktop changes performed.'; return }
if ($HideTimerProbe -and ($OffTransition -or $LockMaximizedWindow)) { throw 'Timer cancellation must be tested separately from OFF-transition experiments.' }
if ($CancelNativeHideTimer -and !$HideTimerProbe) { throw 'Timer cancellation requires the dedicated hide-timer diagnostic.' }
$lockWindow=[IntPtr]::Zero
if ($LockMaximizedWindow) {
    if (!$OffTransition) { throw 'Window locking is available only in the signed-in OFF-transition diagnostic.' }
    $maximized=@([NativeUnlockProbe]::MaximizedWindows())
    if ($maximized.Count -ne 1 -or $maximized[0].Class -ne 'Chrome_WidgetWin_1') { throw 'Requires exactly one visible maximized window, belonging to Chrome, before any diagnostic changes.' }
    $lockWindow=[IntPtr]$maximized[0].Handle
}
$restoreRoundedTB=$false
$master=$null
function Wait-TaskbarMode([bool]$windowsDefault) {
    $timer=[Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 100
        $saved=Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        if ([bool]$saved.IsWindowsDefault -eq $windowsDefault -and $master.Current.IsEnabled) { return }
    } while ($timer.Elapsed.TotalSeconds -lt 10)
    throw "RoundedTB did not finish changing taskbar mode within 10 seconds. Expected Windows default: $windowsDefault"
}
try {
if ($TemporarilyRestoreRoundedTB) {
    $saved=Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    if (!$saved.IsWindowsDefault) {
        if (!$saved.IsDynamic) { throw 'Automatic temporary restore requires the separate-segment layout. Use Restore Taskbar manually for this configuration.' }
        Add-Type -AssemblyName UIAutomationClient
        Add-Type -AssemblyName UIAutomationTypes
        $p=Get-Process RoundedTB -ErrorAction SilentlyContinue | Select-Object -First 1
        if (!$p) { throw 'RoundedTB must already be running for this diagnostic.' }
        Start-Process $p.Path
        $timer=[Diagnostics.Stopwatch]::StartNew()
        do {
            Start-Sleep -Milliseconds 100
            $p=Get-Process RoundedTB -ErrorAction SilentlyContinue | Where-Object {$_.MainWindowHandle -ne 0} | Select-Object -First 1
        } while (!$p -and $timer.Elapsed.TotalSeconds -lt 10)
        if (!$p) { throw 'RoundedTB configuration window did not become available.' }
        $root=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
        $master=$root.FindFirst([Windows.Automation.TreeScope]::Descendants,
            (New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty),'restoreWindowsButton'))
        if (!$master -or !$master.Current.IsEnabled) { throw 'RoundedTB master taskbar control is unavailable or busy.' }
        $restoreRoundedTB=$true
        $master.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        Wait-TaskbarMode $true
    }
}
Assert-WindowsDefault
$bar=[NativeUnlockProbe]::FindWindow('Shell_TrayWnd',$null)
if ($bar -eq [IntPtr]::Zero) { throw 'Windows taskbar not found.' }
$priorDpi=[NativeUnlockProbe]::SetThreadDpiAwarenessContext([IntPtr](-4))
$priorState=[NativeUnlockProbe]::State($bar)
$priorDisplays=[NativeUnlockProbe]::Displays()
$priorCursor=New-Object NativeUnlockProbe+Point
$cursorChanged=$false
if ($HideTimerProbe -and ![NativeUnlockProbe]::GetCursorPos([ref]$priorCursor)) { throw 'Cannot capture original cursor position.' }
$samples=New-Object 'System.Collections.Generic.List[object]'
$writes=New-Object 'System.Collections.Generic.List[object]'
$directory=Join-Path $env:LOCALAPPDATA 'RoundedTB\Diagnostics'
$prefix=if ($HideTimerProbe) {'native-autohide-timer-'} elseif ($OffTransition) {'native-autohide-off-'} else {'native-autohide-'}
$path=Join-Path $directory ($prefix+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')+'.json')
$failure=$null
$changed=$false
$transition=$null
try {
    $begin=[NativeUnlockProbe]::GetTickCount64()
    [NativeUnlockProbe]::SetState($bar,($priorState -bor 1))
    $writes.Add([pscustomobject]@{Operation='NativeAutoHideOn';BeginTick=$begin;EndTick=[NativeUnlockProbe]::GetTickCount64();Utc=[DateTime]::UtcNow.ToString('O');State=($priorState -bor 1)})
    $changed=$true
    if ($HideTimerProbe) {
        # The timer ID is known only for the primary Shell_TrayWnd; use its monitor.
        $native=New-Object NativeUnlockProbe+Rect
        [NativeUnlockProbe]::GetWindowRect($bar,[ref]$native)|Out-Null
        $monitor=[NativeUnlockProbe]::MonitorFromWindow($bar,2).ToInt64()
        $display=$priorDisplays | Where-Object {$_.Handle -eq $monitor} | Select-Object -First 1
        if (!$display) { throw 'Cannot locate the primary taskbar monitor.' }
        $x=[int](($display.Bounds.Left+$display.Bounds.Right)/2)
        $cursorChanged=$true
        if (![NativeUnlockProbe]::SetCursorPos($x,$display.Bounds.Bottom-1)) { throw 'Cannot hover the native activation edge.' }
        $wait=[Diagnostics.Stopwatch]::StartNew()
        do {
            Start-Sleep -Milliseconds 30
            [NativeUnlockProbe]::GetWindowRect($bar,[ref]$native)|Out-Null
        } while ($native.Top -gt $display.Bounds.Bottom-40 -and $wait.Elapsed.TotalSeconds -lt 2)
        if ($native.Top -gt $display.Bounds.Bottom-40) { throw 'Native taskbar did not open sufficiently before timer comparison.' }
        if (![NativeUnlockProbe]::SetCursorPos($x,[int](($display.Bounds.Top+$display.Bounds.Bottom)/2))) { throw 'Cannot move the cursor away for timer comparison.' }
    }
    $watch=[Diagnostics.Stopwatch]::StartNew()
    $nextCheck=0
    $nextTimerCancel=0
    $offApplied=$false
    $state=[NativeUnlockProbe]::State($bar)
    $transitionStateConfirmed=$false
    if ($HideTimerProbe) { Write-Output "READY: Native hide-timer comparison active for $Seconds seconds. Cancellation=$CancelNativeHideTimer. Remain signed in; cursor and native settings will be restored." }
    elseif ($OffTransition) { Write-Output 'READY: Measuring native auto-hide ON to OFF plus one work-area expansion. Remain signed in with Chrome maximized; no lock or window resizing is performed.' }
    else { Write-Output "READY: Native auto-hide diagnostic active for $Seconds seconds. Keep Chrome maximized; manually lock and unlock now. No window resizing or automatic lock is performed. RoundedTB must remain in Windows default mode." }
    while ($watch.Elapsed.TotalSeconds -lt $Seconds) {
        if ($watch.ElapsedMilliseconds -ge $nextCheck) { Assert-WindowsDefault; $nextCheck=$watch.ElapsedMilliseconds+1000 }
        if ($CancelNativeHideTimer -and $watch.ElapsedMilliseconds -ge $nextTimerCancel) {
            $result=[NativeUnlockProbe]::CancelHideTimer($bar)
            $writes.Add([pscustomobject]@{Operation='KillNativeHideTimer2';BeginTick=$result.BeginTick;EndTick=$result.EndTick;Success=$result.Success;LastError=$result.LastError})
            $nextTimerCancel=$watch.ElapsedMilliseconds+100
        }
        if ($OffTransition -and !$offApplied -and $watch.ElapsedMilliseconds -ge 1500) {
            $offState=$priorState -band (-bnot 1)
            if ($LockMaximizedWindow) {
                $maximized=@([NativeUnlockProbe]::MaximizedWindows())
                if ($maximized.Count -ne 1 -or $maximized[0].Handle -ne $lockWindow.ToInt64()) { throw 'The single maximized Chrome window changed; window-lock diagnostic is inconclusive.' }
            }
            $transition=New-Object NativeUnlockProbe+Transition ($bar,[uint32]$offState,$ExpansionFlags,$lockWindow)
            $offApplied=$true
        }
        $sampleBegin=[NativeUnlockProbe]::GetTickCount64()
        if (!$OffTransition) { $state=[NativeUnlockProbe]::State($bar) }
        elseif ($transition -and $transition.Completed -and !$transitionStateConfirmed) {
            if ($transition.Error) { throw $transition.Error }
            $state=[NativeUnlockProbe]::State($bar)
            $transitionStateConfirmed=$true
        }
        if (!$offApplied -and ($state -band 1) -eq 0) { throw 'Windows did not retain native auto-hide; diagnostic is inconclusive.' }
        if ($transitionStateConfirmed -and ($state -band 1) -ne 0) { throw 'Windows did not turn native auto-hide off; diagnostic is inconclusive.' }
        $rect=New-Object NativeUnlockProbe+Rect
        [NativeUnlockProbe]::GetWindowRect($bar,[ref]$rect)|Out-Null
        $displays=[NativeUnlockProbe]::Displays()
        $windows=[NativeUnlockProbe]::MaximizedWindows()
        $samples.Add([pscustomobject]@{Utc=[DateTime]::UtcNow.ToString('O');Milliseconds=$watch.ElapsedMilliseconds;BeginTick=$sampleBegin;EndTick=[NativeUnlockProbe]::GetTickCount64();AppbarState=$state;StateConfirmed=(!$offApplied -or $transitionStateConfirmed);OffApplied=$offApplied;Taskbar=$rect;Displays=$displays;Windows=$windows})
        Start-Sleep -Milliseconds 30
    }
} catch { $failure=$_.Exception.Message; throw }
finally {
    try {
        if ($transition) {
            if (!$transition.CancelAndJoin()) {
                $failure='Inconclusive: a native API did not return within the cleanup timeout; remaining monitor writes were cancelled.'
                Write-Warning $failure
                $transition.ReleaseWindowLock()
            }
            foreach ($write in $transition.Writes.ToArray()) { $writes.Add($write) }
            if ($transition.Error -and !$failure) { $failure=$transition.Error }
        }
        if ($changed) {
            [NativeUnlockProbe]::SetState($bar,$priorState)
            foreach ($display in $priorDisplays) {
                # Do not overwrite a newly attached/reconfigured monitor's work area.
                $current=[NativeUnlockProbe]::Displays() | Where-Object { $_.Handle -eq $display.Handle -and $_.Bounds.Equals($display.Bounds) } | Select-Object -First 1
                if ($current -and !( [NativeUnlockProbe]::RestoreWork($display.Work))) { Write-Warning 'Could not restore a saved work area.' }
            }
        }
    } finally {
        if ($cursorChanged) { [NativeUnlockProbe]::SetCursorPos($priorCursor.X,$priorCursor.Y)|Out-Null }
        [NativeUnlockProbe]::SetThreadDpiAwarenessContext($priorDpi)|Out-Null
        New-Item -ItemType Directory -Path $directory -Force|Out-Null
        $mode=if ($HideTimerProbe) {'NativeAutoHideTimerCancellation'} elseif ($OffTransition) {'NativeAutoHideOffThenSingleExpansion'} else {'NativeAutoHideWithoutRoundedTBExpansion'}
        [pscustomobject]@{Mode=$mode;Seconds=$Seconds;CancelNativeHideTimer=[bool]$CancelNativeHideTimer;ExpansionFlags=$ExpansionFlags;LockedWindow=$lockWindow.ToInt64();Failure=$failure;OriginalState=$priorState;Writes=$writes.ToArray();Samples=$samples.ToArray()} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $path -Encoding UTF8
        Write-Output "Restored native taskbar settings. Geometry saved to $path"
    }
}
} finally {
    if ($restoreRoundedTB -and (Get-Process RoundedTB -ErrorAction SilentlyContinue)) {
        $saved=Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        if ($saved.IsWindowsDefault) {
            Wait-TaskbarMode $true
            $master.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
            Wait-TaskbarMode $false
            Write-Output 'RoundedTB enabled again with its existing preferences.'
        }
    }
}
