param([string]$Version = '1.0.2', [string]$Compiler)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Version must contain three or four numeric components.' }
$root = Split-Path $PSScriptRoot -Parent
if (!(Test-Path "$root\artifacts\RoundedTB\RoundedTB.exe")) { throw 'Publish the Release win-x64 application first.' }
if (!$Compiler) {
    $Compiler = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (!(Test-Path $Compiler)) { $Compiler = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" }
}
if (!(Test-Path $Compiler)) { throw 'Install Inno Setup 6, or supply -Compiler with the path to ISCC.exe.' }
& $Compiler "/DAppVersion=$Version" "$root\packaging\RoundedTB.iss"
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed: $LASTEXITCODE" }
Get-Item "$root\artifacts\installer\RoundedTB-Setup-$Version-win-x64.exe"
