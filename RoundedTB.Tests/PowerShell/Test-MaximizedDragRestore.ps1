$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing @'
using System;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;
public static class MaximizedDragProbe {
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)] public struct Point {public int X,Y;}
 [StructLayout(LayoutKind.Sequential)] public struct Gui {public uint Size,Flags;public IntPtr Active,Focus,Capture,Menu,Move,Caret;public Rect CaretRect;}
 [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls,string name);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint id,ref Gui gui);
 [DllImport("user32.dll")] static extern bool GetCursorPos(out Point p);
 [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
 [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr h,IntPtr r);
 [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int a,int b,int c,int d);
 [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
 public static string Run() {
  Point original;GetCursorPos(out original);
  var bar=FindWindow("Shell_TrayWnd",null);
  var region=CreateRectRgn(0,0,0,0);
  string result=null;
  using(var rear=new Form {Text="RoundedTB rear maximized test",WindowState=FormWindowState.Maximized})
  using(var front=new Form {Text="RoundedTB drag test",WindowState=FormWindowState.Maximized}) {
   rear.Show();
   front.Shown+=(s,e)=> {
    var frontHandle=front.Handle;
    var rearHandle=rear.Handle;
    new Thread(()=> {
     bool held=false;
     try {
      Thread.Sleep(700);
      if(GetWindowRgn(bar,region)!=0) throw new Exception("Taskbar was not restored with both windows maximized.");
      Rect caption;GetWindowRect(frontHandle,out caption);
      SetCursorPos(caption.Left+350,caption.Top+20);
      mouse_event(2,0,0,0,UIntPtr.Zero);held=true;
      Thread.Sleep(100);
      SetCursorPos(caption.Left+550,caption.Top+250);
      Thread.Sleep(700);
      var gui=new Gui {Size=(uint)Marshal.SizeOf(typeof(Gui))};
      GetGUIThreadInfo(0,ref gui);
      if((gui.Flags&2)==0 || gui.Move!=frontHandle) throw new Exception("Could not enter the front test window's move loop.");
      if(GetWindowRgn(bar,region)!=0) throw new Exception("Dragging one maximized window removed restoration despite another maximized window.");
      result="Taskbar stayed restored while dragging one of two maximized windows.";
     } catch(Exception error) {result="FAIL: "+error.Message;}
     finally {
      if(held) mouse_event(4,0,0,0,UIntPtr.Zero);
      SetCursorPos(original.X,original.Y);
      front.BeginInvoke(new Action(()=>{rear.Close();front.Close();}));
     }
    }) {IsBackground=true}.Start();
   };
   Application.Run(front);
  }
  DeleteObject(region);
  return result;
 }
}
'@
$settings=Get-Content (Join-Path $env:LOCALAPPDATA 'rtb.json')|ConvertFrom-Json
if($settings.IsWindowsDefault){throw 'Requires active RoundedTB.'}
Start-Process (Join-Path $env:LOCALAPPDATA 'Programs\RoundedTB\RoundedTB.exe')
Start-Sleep -Milliseconds 600
$p=Get-Process RoundedTB|Where-Object {$_.MainWindowHandle -ne 0}|Select-Object -First 1
$root=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
function Control($id){$root.FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty),$id))}
function SelectSection($id){$root.FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty),$id)).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()}
function Toggle($id,$value){SelectSection 'generalNavigationButton';$t=(Control $id).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern);if(($t.Current.ToggleState -eq [Windows.Automation.ToggleState]::On) -ne $value){$t.Toggle()}}
function WaitForLiveSettings { Start-Sleep -Milliseconds 300 }
SelectSection 'generalNavigationButton'
try {
 Toggle 'expandWorkAreaCheckBox' $false
 Toggle 'fillMaximisedCheckBox' $true
 WaitForLiveSettings
 $result=[MaximizedDragProbe]::Run()
 if($result.StartsWith('FAIL:')){throw $result}
 Write-Output $result
} finally {
 Toggle 'expandWorkAreaCheckBox' ([bool]$settings.ExpandMaximizedWorkArea)
 Toggle 'fillMaximisedCheckBox' ([bool]$settings.FillOnMaximise)
 WaitForLiveSettings
}
