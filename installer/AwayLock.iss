#define AppVersion "1.0.2"
#define RepoRoot AddBackslash(SourcePath) + ".."

[Setup]
AppId={{99ECBCBF-473D-45F6-B32D-37CF35305836}
AppName=AwayLock
AppVersion={#AppVersion}
AppPublisher=Xukun Qin
AppPublisherURL=https://github.com/xkqin/awaylock
AppSupportURL=https://github.com/xkqin/awaylock/issues
AppUpdatesURL=https://github.com/xkqin/awaylock/releases
DefaultDirName={localappdata}\Programs\AwayLock
DefaultGroupName=AwayLock
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
WizardStyle=modern
SetupIconFile={#RepoRoot}\src\AwayLock\Assets\AwayLock.ico
UninstallDisplayIcon={app}\AwayLock.exe
LicenseFile={#RepoRoot}\LICENSE
OutputDir={#RepoRoot}\artifacts
OutputBaseFilename=AwayLock-Setup-{#AppVersion}-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[CustomMessages]
english.LaunchProgram=Open AwayLock
chinesesimp.LaunchProgram=启动离身锁 AwayLock

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#RepoRoot}\artifacts\distribution\AwayLock.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\artifacts\distribution\AwayLock.Agent.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\artifacts\distribution\Notices\*"; DestDir: "{app}\Notices"; Flags: ignoreversion
Source: "{#RepoRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\AwayLock"; Filename: "{app}\AwayLock.exe"
Name: "{autodesktop}\AwayLock"; Filename: "{app}\AwayLock.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AwayLock.exe"; Parameters: "--resume-protection"; Flags: runhidden waituntilterminated
Filename: "{app}\AwayLock.exe"; Description: "{cm:LaunchProgram}"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{app}\AwayLock.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveAwayLockAgent"

[UninstallDelete]
Type: files; Name: "{localappdata}\AwayLock\settings.json"
Type: files; Name: "{localappdata}\AwayLock\agent-state.json"
Type: files; Name: "{localappdata}\AwayLock\agent-error.log"
Type: files; Name: "{localappdata}\AwayLock\agent-events.jsonl"
Type: files; Name: "{localappdata}\AwayLock\agent-events.jsonl.previous"
Type: dirifempty; Name: "{localappdata}\AwayLock"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  ExistingApp: String;
begin
  Result := '';
  ExistingApp := ExpandConstant('{app}\AwayLock.exe');
  if FileExists(ExistingApp) then
  begin
    if not Exec(ExistingApp, '--prepare-update', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      Result := 'AwayLock could not prepare its background agent for the update.'
    else if ExitCode <> 0 then
      Result := 'Please stop AwayLock protection before installing the update.';
  end;
end;
