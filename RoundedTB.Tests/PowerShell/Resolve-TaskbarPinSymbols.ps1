param([string]$OutputPath,[switch]$CompileOnly)
$ErrorActionPreference='Stop'
# Development diagnostic only: resolve exact-build Microsoft symbols before
# attaching any hooks. No Explorer process or taskbar state is changed.
Add-Type @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
public static class TaskbarSymbolProbe {
 public class Symbol { public string Name; public ulong Rva; }
 public class Module { public string Path,Name; public uint Timestamp,ImageSize; public Dictionary<string,Symbol> Symbols; }
 private delegate bool EnumSymbol(IntPtr info,uint size,IntPtr context);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern IntPtr LoadLibrary(string path);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern bool SetDllDirectory(string path);
 [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
 [DllImport("dbghelp.dll",SetLastError=true,CharSet=CharSet.Ansi)] private static extern bool SymInitialize(IntPtr process,string path,bool invade);
 [DllImport("dbghelp.dll")] private static extern uint SymSetOptions(uint options);
 [DllImport("dbghelp.dll",SetLastError=true,CharSet=CharSet.Ansi)] private static extern ulong SymLoadModuleEx(IntPtr process,IntPtr file,string image,string module,ulong address,uint size,IntPtr data,uint flags);
 [DllImport("dbghelp.dll",SetLastError=true,CharSet=CharSet.Ansi)] private static extern bool SymEnumSymbols(IntPtr process,ulong address,string mask,EnumSymbol callback,IntPtr context);
 [DllImport("dbghelp.dll",CharSet=CharSet.Ansi)] private static extern uint UnDecorateSymbolName(string name,StringBuilder result,uint size,uint flags);
 [DllImport("dbghelp.dll")] private static extern bool SymCleanup(IntPtr process);
 public static Module[] Resolve(string cache,string taskbar,string view) {
  Directory.CreateDirectory(cache);
  IntPtr process=GetCurrentProcess();
  SymSetOptions(0x00000004|0x00000200|0x00000400|0x00080000);
  if(!SymInitialize(process,"srv*"+cache+"*https://msdl.microsoft.com/download/symbols",false)) throw new Win32Exception(Marshal.GetLastWin32Error(),"SymInitialize");
  try {
   return new[] { ResolveModule(process,taskbar,new Dictionary<string,string>{{"Hide","TrayUI::_Hide"},{"SecondaryAutoHide","CSecondaryTray::_AutoHide"}}),
                  ResolveModule(process,view,new Dictionary<string,string>{{"ShouldExpand","ViewCoordinator::ShouldTaskbarBeExpanded"},{"UpdateIsExpanded","ViewCoordinator::UpdateIsExpanded"}}) };
  } finally {SymCleanup(process);}
 }
 private static Module ResolveModule(IntPtr process,string path,Dictionary<string,string> wanted) {
  byte[] pe=File.ReadAllBytes(path); int header=BitConverter.ToInt32(pe,0x3c);
  if(BitConverter.ToUInt32(pe,header)!=0x4550 || BitConverter.ToUInt16(pe,header+4)!=0x8664) throw new InvalidOperationException("Not an x64 PE: "+path);
  var result=new Module {Path=path,Name=System.IO.Path.GetFileName(path),Timestamp=BitConverter.ToUInt32(pe,header+8),ImageSize=BitConverter.ToUInt32(pe,header+24+56),Symbols=new Dictionary<string,Symbol>()};
  ulong address=SymLoadModuleEx(process,IntPtr.Zero,path,null,result.Name.Equals("taskbar.dll",StringComparison.OrdinalIgnoreCase) ? 0x180000000UL : 0x200000000UL,result.ImageSize,IntPtr.Zero,0);
  if(address==0) throw new Win32Exception(Marshal.GetLastWin32Error(),"SymLoadModuleEx: "+path);
  EnumSymbol callback=delegate(IntPtr info,uint size,IntPtr context) {
   uint length=(uint)Marshal.ReadInt32(info,76);
   string name=Marshal.PtrToStringAnsi(new IntPtr(info.ToInt64()+84),(int)length);
   var text=new StringBuilder(4096);
   if(UnDecorateSymbolName(name,text,4096,0)!=0) name=text.ToString();
   foreach(var item in wanted) {
    int match=name.IndexOf(item.Value,StringComparison.Ordinal);
    if(match<0) continue;
    int end=match+item.Value.Length;
    if(end<name.Length && name[end]!='(' && !Char.IsWhiteSpace(name[end])) continue;
    ulong target=unchecked((ulong)Marshal.ReadInt64(info,56));
    if(target<address || target-address>=result.ImageSize) throw new InvalidOperationException("Symbol outside module: "+name);
    if(result.Symbols.ContainsKey(item.Key) && result.Symbols[item.Key].Rva!=target-address) throw new InvalidOperationException("Ambiguous symbol: "+item.Key);
    result.Symbols[item.Key]=new Symbol {Name=name,Rva=target-address};
   }
   return true;
  };
  if(!SymEnumSymbols(process,address,"*",callback,IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(),"SymEnumSymbols: "+path);
  foreach(var item in wanted) if(!result.Symbols.ContainsKey(item.Key)) throw new InvalidOperationException("Exact-build symbol unavailable: "+item.Value+" in "+path);
  return result;
 }
}
'@
if ($CompileOnly) { Write-Output 'Symbol resolver compiled; no desktop or symbol-cache changes.'; return }
$debuggers=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Debuggers\x64'
$dbghelp=Join-Path $debuggers 'dbghelp.dll'
if (!(Test-Path $dbghelp) -or !(Test-Path (Join-Path $debuggers 'symsrv.dll'))) { throw 'Development symbol resolution requires Windows SDK Debugging Tools for x64.' }
if (![TaskbarSymbolProbe]::SetDllDirectory($debuggers) -or [TaskbarSymbolProbe]::LoadLibrary($dbghelp) -eq [IntPtr]::Zero) { throw 'Cannot load Windows SDK symbol resolver.' }
try {
 $cache=Join-Path $env:LOCALAPPDATA 'RoundedTB\NativeSymbols'
 $taskbar=Join-Path $env:WINDIR 'System32\taskbar.dll'
 $view=Join-Path $env:WINDIR 'SystemApps\MicrosoftWindows.Client.Core_cw5n1h2txyewy\Taskbar.View.dll'
 $modules=[TaskbarSymbolProbe]::Resolve($cache,$taskbar,$view)
 $json=[pscustomobject]@{Version=1;Modules=$modules} | ConvertTo-Json -Depth 8
 if ($OutputPath) { [IO.File]::WriteAllText($OutputPath,$json) }
 $json
} finally { [TaskbarSymbolProbe]::SetDllDirectory($null)|Out-Null }
