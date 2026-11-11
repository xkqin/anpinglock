#define AppVersion "1.1.0"
#define RepoRoot AddBackslash(SourcePath) + ".."

[Setup]
AppId={{99ECBCBF-473D-45F6-B32D-37CF35305836}
AppName=安屏 AnpingLock
AppVersion={#AppVersion}
AppPublisher=Xukun Qin
AppPublisherURL=https://github.com/xkqin/anpinglock
AppSupportURL=https://github.com/xkqin/anpinglock/issues
AppUpdatesURL=https://github.com/xkqin/anpinglock/releases
DefaultDirName={localappdata}\Programs\AnpingLock
UsePreviousAppDir=no
DefaultGroupName=安屏 AnpingLock
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
WizardStyle=modern
SetupIconFile={#RepoRoot}\src\AnpingLock\Assets\AnpingLock.ico
UninstallDisplayIcon={app}\AnpingLock.exe
LicenseFile={#RepoRoot}\LICENSE
OutputDir={#RepoRoot}\artifacts
OutputBaseFilename=AnpingLock-Setup-{#AppVersion}-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[CustomMessages]
english.LaunchProgram=Open AnpingLock
chinesesimp.LaunchProgram=启动安屏 AnpingLock

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#RepoRoot}\artifacts\distribution\AnpingLock.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\artifacts\distribution\AnpingLock.Agent.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\artifacts\distribution\Notices\*"; DestDir: "{app}\Notices"; Flags: ignoreversion
Source: "{#RepoRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\安屏 AnpingLock"; Filename: "{app}\AnpingLock.exe"
Name: "{autodesktop}\安屏 AnpingLock"; Filename: "{app}\AnpingLock.exe"; Tasks: desktopicon

[InstallDelete]
Type: files; Name: "{autoprograms}\AwayLock.lnk"
Type: files; Name: "{autodesktop}\AwayLock.lnk"

[Run]
Filename: "{app}\AnpingLock.exe"; Parameters: "--resume-protection"; Flags: runhidden waituntilterminated
Filename: "{app}\AnpingLock.exe"; Description: "{cm:LaunchProgram}"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{app}\AnpingLock.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveAnpingLockAgent"

[UninstallDelete]
Type: files; Name: "{localappdata}\AnpingLock\settings.json"
Type: files; Name: "{localappdata}\AnpingLock\agent-state.json"
Type: files; Name: "{localappdata}\AnpingLock\agent-error.log"
Type: files; Name: "{localappdata}\AnpingLock\startup-error.log"
Type: files; Name: "{localappdata}\AnpingLock\agent-events.jsonl"
Type: files; Name: "{localappdata}\AnpingLock\agent-events.jsonl.previous"
Type: dirifempty; Name: "{localappdata}\AnpingLock"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  ExistingApp: String;
  OldSettings: String;
  NewSettings: String;
begin
  Result := '';
  { Use the bundled helper; older versions cannot reliably remove an already-missing task. }
  ExtractTemporaryFile('AnpingLock.exe');
  ExistingApp := ExpandConstant('{tmp}\AnpingLock.exe');
  if not Exec(ExistingApp, '--prepare-update', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Result := 'AnpingLock could not prepare its background agent for the update.'
  else if ExitCode <> 0 then
    Result := 'Please stop protection before installing the update.';
  if Result <> '' then Exit;

  { Copy only preferences. Do not recover a stale lock countdown across the rename. }
  OldSettings := ExpandConstant('{localappdata}\AwayLock\settings.json');
  NewSettings := ExpandConstant('{localappdata}\AnpingLock\settings.json');
  if FileExists(OldSettings) and not FileExists(NewSettings) then
  begin
    if not ForceDirectories(ExtractFileDir(NewSettings)) then
      Result := 'Could not create the AnpingLock preferences directory.'
    else if not CopyFile(OldSettings, NewSettings, True) then
      Result := 'Could not migrate your existing preferences.';
  end;
end;
