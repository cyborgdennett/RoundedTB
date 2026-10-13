#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define RepoRoot SourcePath + ".."
[Setup]
AppId={{B8DBF5D1-CF65-4736-9556-711CBE407756}
AppName=RoundedTB
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\RoundedTB
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#RepoRoot}\artifacts\installer
OutputBaseFilename=RoundedTB-Setup-{#AppVersion}-win-x64
SetupIconFile={#RepoRoot}\RoundedTB\RoundedTB.ico
UninstallDisplayIcon={app}\RoundedTB.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
[Files]
Source: "{#RepoRoot}\artifacts\RoundedTB\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "{#RepoRoot}\scripts\install.ps1"; DestDir: "{tmp}"; Flags: dontcopy
Source: "{#RepoRoot}\packaging\uninstall.ps1"; DestDir: "{app}"
[Tasks]
Name: startup; Description: "Start RoundedTB when I sign in"; Flags: unchecked
[Icons]
Name: "{userprograms}\RoundedTB"; Filename: "{app}\RoundedTB.exe"
[Run]
Filename: "{app}\RoundedTB.exe"; Description: "Open RoundedTB"; Flags: nowait postinstall skipifsilent
[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\uninstall.ps1"" -Destination ""{app}"""; Flags: runhidden waituntilterminated; RunOnceId: CleanupRoundedTB
[Code]
procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  Confirm := False;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer; Args: String;
begin
  if CurStep = ssPostInstall then begin
    Args := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\install.ps1') +
      '" -Source "' + ExpandConstant('{app}') + '" -Destination "' + ExpandConstant('{app}') +
      '" -ConfigureOnly -NoLaunch';
    if WizardIsTaskSelected('startup') then Args := Args + ' -EnableStartup';
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Args,
      '', SW_HIDE, ewWaitUntilTerminated, Code) then
      RaiseException('Unable to configure RoundedTB.')
    else if Code <> 0 then RaiseException('RoundedTB configuration failed. Run setup again.');
  end;
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  ExtractTemporaryFile('install.ps1');
  if FileExists(ExpandConstant('{app}\RoundedTB.exe')) then
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\install.ps1') +
      '" -Source "' + ExpandConstant('{app}') + '" -Destination "' + ExpandConstant('{app}') +
      '" -StopOnly -NoLaunch', '', SW_HIDE, ewWaitUntilTerminated, Code) then
      Result := 'Unable to stop RoundedTB before updating.'
    else if Code <> 0 then Result := 'RoundedTB could not be stopped safely. Close it from the tray and retry.';
end;
