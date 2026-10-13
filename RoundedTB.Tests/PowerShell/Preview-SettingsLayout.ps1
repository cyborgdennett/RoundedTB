param([string]$OutputDirectory=(Join-Path $PSScriptRoot '..\..\..\artifacts\settings-preview'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SettingsPreview {
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
'@
Start-Process (Join-Path $env:LOCALAPPDATA 'Programs\RoundedTB\RoundedTB.exe')
Start-Sleep -Seconds 3
$p=Get-Process RoundedTB|Where-Object {$_.MainWindowHandle -ne 0}|Select-Object -First 1
$root=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
[SettingsPreview]::SetForegroundWindow($p.MainWindowHandle)|Out-Null
Start-Sleep -Milliseconds 300
if([SettingsPreview]::GetForegroundWindow() -ne $p.MainWindowHandle){throw 'Focus RoundedTB before capturing its pages.'}
New-Item -ItemType Directory $OutputDirectory -Force|Out-Null
$prior=[SettingsPreview]::SetThreadDpiAwarenessContext([IntPtr](-4))
try {
 foreach($name in @('general','appearance','notifications','windows','advanced')) {
  $tab=$root.FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty),($name+'NavigationButton')))
  $tab.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 300
  $rect=New-Object SettingsPreview+Rect
  [SettingsPreview]::GetWindowRect($p.MainWindowHandle,[ref]$rect)|Out-Null
  $bitmap=New-Object Drawing.Bitmap ($rect.Right-$rect.Left),($rect.Bottom-$rect.Top)
  $graphics=[Drawing.Graphics]::FromImage($bitmap)
  $graphics.CopyFromScreen($rect.Left,$rect.Top,0,0,$bitmap.Size)
  try {$bitmap.Save((Join-Path $OutputDirectory ($name+'.png')),[Drawing.Imaging.ImageFormat]::Png)}finally{$graphics.Dispose();$bitmap.Dispose()}
  Write-Output ('Captured '+$name)
 }
 $tab=$root.FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty),'generalNavigationButton'))
 $tab.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
} finally {[SettingsPreview]::SetThreadDpiAwarenessContext($prior)|Out-Null}
