$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path $vswhere)) { throw 'Visual Studio C++ Build Tools are required for this prototype.' }
$msbuild=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'No Visual Studio installation with the x64 C++ compiler was found.' }
& $msbuild (Join-Path $repo 'native\TaskbarPin\TaskbarPin.vcxproj') '/p:Configuration=Release' '/p:Platform=x64' '/m:1' '/nologo' '/verbosity:minimal'
if ($LASTEXITCODE -ne 0) { throw "Native prototype build failed with exit code $LASTEXITCODE." }
Get-Item (Join-Path $repo 'artifacts\native-pin\RoundedTB.TaskbarPin.dll') | Select-Object FullName,Length
