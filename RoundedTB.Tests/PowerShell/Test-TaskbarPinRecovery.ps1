$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path (Join-Path $repo 'native\TaskbarPin\PrototypeController.cs')
$bar=[TaskbarPinController]::FindTaskbar()
$before=[TaskbarPinController]::NativeState($bar)
if (($before -band 1) -ne 0) { throw 'Native auto-hide must be off before the controller-death test.' }
$script=Join-Path $PSScriptRoot 'Test-TaskbarPinPrototype.ps1'
$output=Join-Path $repo 'artifacts\native-pin\recovery-child.log'
$errors=Join-Path $repo 'artifacts\native-pin\recovery-child-errors.log'
$child=$null; $mapping=$null; $view=$null
try {
 $child=Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"{0}"' -f $script),'-Seconds','120') -WindowStyle Hidden -RedirectStandardOutput $output -RedirectStandardError $errors -PassThru
 $timer=[Diagnostics.Stopwatch]::StartNew()
 do {
  if ($child.HasExited) { throw "Prototype child exited before ready. See $errors" }
  if (!$mapping) {
   try { $mapping=[IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting("Local\RoundedTB.TaskbarPin.$($child.Id)") } catch [IO.FileNotFoundException] { }
   if ($mapping) { $view=$mapping.CreateViewAccessor(0,352,[IO.MemoryMappedFiles.MemoryMappedFileAccess]::ReadWrite) }
  }
  if ($view -and $view.ReadInt32(72) -eq 3) { throw "Native child initialization failed: $($view.ReadUInt32(76))" }
  $ready=$view -and $view.ReadInt32(72) -eq 1 -and ([TaskbarPinController]::NativeState($bar) -band 1) -ne 0
  if (!$ready) { Start-Sleep -Milliseconds 50 }
 } while (!$ready -and $timer.Elapsed.TotalSeconds -lt 30)
 if (!$ready) { throw 'Prototype did not reach native auto-hide ON within 30 seconds.' }
 $death=[Diagnostics.Stopwatch]::StartNew()
 Stop-Process -Id $child.Id -Force
 do {
  $state=[TaskbarPinController]::NativeState($bar)
  $status=$view.ReadInt32(72)
  if ($state -eq $before -and $status -eq 2) { break }
  Start-Sleep -Milliseconds 20
 } while ($death.Elapsed.TotalSeconds -lt 10)
 if ($state -ne $before -or $status -ne 2) { throw "Controller-death cleanup failed: state=$state, status=$status, error=$($view.ReadUInt32(76))." }
 Write-Output "Controller was terminated while native auto-hide was ON. Watchdog restored native state $state and disabled hooks in $($death.ElapsedMilliseconds) ms."
} finally {
 if ($view) { $view.Write(68,0) }
 if ($child -and !$child.HasExited) { Stop-Process -Id $child.Id -Force }
 if ($view) { $view.Dispose() }
 if ($mapping) { $mapping.Dispose() }
 if ($child) { $child.Dispose() }
}
