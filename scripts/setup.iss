; AI Usage Tracker - Inno Setup Script

#ifndef MyAppVersion
  #define MyAppVersion "2.3.4-beta.31"
#endif
#ifndef SourcePath
  #define SourcePath "..\dist\publish-win-x64"
#endif
#ifndef MyAppArch
  #define MyAppArch "x64"
#endif
#ifndef InstallerCompression
  #define InstallerCompression "balanced"
#endif

#include "inno\CodeDependencies.iss"

[Code]
var
  RestartApplications: Boolean;
  DeleteDatabase: Boolean;

procedure ConfigureRuntimeDependencies();
var
  NeedsDesktopRuntime: Boolean;
  NeedsAspNetRuntime: Boolean;
  NeedsNetRuntime: Boolean;
begin
  SetArrayLength(Dependency_List, 0);
  Dependency_Memo := '';

  NeedsDesktopRuntime := WizardIsComponentSelected('apps\tracker');
  NeedsAspNetRuntime := WizardIsComponentSelected('apps\monitor') or WizardIsComponentSelected('apps\web');
  NeedsNetRuntime := WizardIsComponentSelected('apps\cli') and (not NeedsDesktopRuntime) and (not NeedsAspNetRuntime);

  if NeedsDesktopRuntime then
  begin
    Dependency_AddDotNet80Desktop;
  end;

  if NeedsAspNetRuntime then
  begin
    Dependency_AddDotNet80Asp;
  end;

  if NeedsNetRuntime then
  begin
    Dependency_AddDotNet80;
  end;
end;

function InitializeSetup(): Boolean;
var
  Arch: String;
  I: Integer;
  Param: String;
begin
  Result := True;
  Arch := '{#MyAppArch}';
  RestartApplications := False;

  // Check for /RESTARTAPPLICATIONS parameter
  for I := 1 to ParamCount do
  begin
    Param := ParamStr(I);
    if CompareText(Param, '/RESTARTAPPLICATIONS') = 0 then
    begin
      RestartApplications := True;
      Break;
    end;
  end;

  // Validate architecture match
  if Arch = 'x64' then
  begin
    if not Is64BitInstallMode then
    begin
      MsgBox('Error: Attempting to install x64 installer on a non-64-bit system.' + #13 + 'Please download the correct installer.', mbError, MB_OK);
      Result := False;
    end;
  end
  else if Arch = 'x86' then
  begin
    // x86 usually runs on anything, but we can add checks if needed
  end
  else if Arch = 'arm64' then
  begin
    if ProcessorArchitecture <> paARM64 then
    begin
      MsgBox('Error: Attempting to install arm64 installer on a non-ARM64 system.' + #13 + 'Please download the correct installer.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpReady then
  begin
    ConfigureRuntimeDependencies();
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  ConfigureRuntimeDependencies();

  // Explicitly stop running instances before files are overwritten.
  // CloseApplications=yes relies on Restart Manager (WM_QUERYENDSESSION) which fails
  // for tray/background processes that have no message pump. taskkill /F is reliable.
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM AIUsageTracker.exe /T',        '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM AIUsageTracker.Monitor.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM AIUsageTracker.Web.exe /T',     '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM AIUsageTracker.CLI.exe /T',     '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Brief pause to let the OS release file handles after process exit.
  Sleep(500);

  Result := '';
end;

function ShouldRunApplication(): Boolean;
begin
  // Always offer launch option for interactive installs.
  // With postinstall + default checked, /RESTARTAPPLICATIONS flow still relaunches.
  Result := True;
end;

function InitializeUninstall(): Boolean;
var
  DatabasePath: String;
  DatabaseExists: Boolean;
begin
  Result := True;
  DeleteDatabase := False;

  // Check if data directory exists (contains Monitor database and UI.Slim preferences)
  DatabasePath := ExpandConstant('{localappdata}\AIUsageTracker');
  DatabaseExists := DirExists(DatabasePath);

  if DatabaseExists and not UninstallSilent then
  begin
    if MsgBox('Do you want to delete your AI Usage Tracker data? This includes your usage history, settings, and preferences.' + #13#10#13#10 +
              'Location: ' + DatabasePath + #13#10#13#10 +
              'Click Yes to delete all data, or No to keep it for future use.',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      DeleteDatabase := True;
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataPath: String;
begin
  if (CurUninstallStep = usPostUninstall) and DeleteDatabase then
  begin
    // Delete the entire AIUsageTracker directory including Monitor and UI.Slim subdirectories
    DataPath := ExpandConstant('{localappdata}\AIUsageTracker');
    if DirExists(DataPath) then
    begin
      DelTree(DataPath, True, True, True);
    end;
  end;
end;

[Setup]
AppId={{D3B3E8A1-8E9D-4F6B-A2B3-7C8D9E0F1A2B}
AppName=AI Usage Tracker
AppVersion={#MyAppVersion}
AppPublisher=Alexander Brandt
AppPublisherURL=https://github.com/rygel/AIUsageTracker
AppSupportURL=https://github.com/rygel/AIUsageTracker
AppUpdatesURL=https://github.com/rygel/AIUsageTracker/releases
AlwaysShowComponentsList=yes
DefaultDirName={autopf}\AIUsageTracker
DefaultGroupName=AI Usage Tracker
OutputDir=..\dist
OutputBaseFilename=AIUsageTracker_Setup_v{#MyAppVersion}_{#MyAppArch}
; Installer compression profile:
; - balanced: lzma2/normal + non-solid (default, good size/perf and safer AV heuristics)
; - max: lzma2/ultra64 + solid (smallest size, can increase AV false-positive risk)
; - compat: zip + non-solid (largest size, best compatibility)
#if InstallerCompression == "max"
Compression=lzma2/ultra64
SolidCompression=yes
#elif InstallerCompression == "compat"
Compression=zip
SolidCompression=no
#else
Compression=lzma2/normal
SolidCompression=no
#endif
CloseApplications=yes
CloseApplicationsFilter=AIUsageTracker*.exe
DisableDirPage=auto
DirExistsWarning=no
SetupIconFile=..\AIUsageTracker.UI.Slim\Assets\app_icon.ico
UninstallDisplayIcon={app}\AIUsageTracker.exe
PrivilegesRequired=lowest

#if MyAppArch == "x64"
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
#elif MyAppArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Full installation"
Name: "compact"; Description: "Compact installation"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "apps"; Description: "Applications"; Types: full compact custom; Flags: fixed
Name: "apps\tracker"; Description: "AI Usage Tracker UI"; Types: full compact custom
Name: "apps\monitor"; Description: "AI Usage Tracker Monitor"; Types: full custom
Name: "apps\web"; Description: "AI Usage Tracker Web UI"; Types: full custom
Name: "apps\cli"; Description: "AI Usage Tracker CLI"; Types: full compact custom

[Tasks]
Name: "desktopicontracker"; Description: "Create AI Usage Tracker UI desktop icon"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; Components: apps\tracker
Name: "startupmonitor"; Description: "Run AI Usage Tracker Monitor at Windows Startup"; GroupDescription: "Additional options:"; Flags: unchecked; Components: apps\monitor
Name: "startuptracker"; Description: "Run AI Usage Tracker UI at Windows Startup"; GroupDescription: "Additional options:"; Flags: unchecked; Components: apps\tracker

[Files]
Source: "..\AIUsageTracker.UI.Slim\Assets\app_icon.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\README.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourcePath}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "inno\LICENSE.InnoDependencyInstaller.txt"; DestDir: "{app}\THIRD-PARTY-LICENSES"; DestName: "InnoDependencyInstaller.txt"; Flags: ignoreversion
Source: "{#SourcePath}\Tracker\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: apps\tracker
Source: "{#SourcePath}\Monitor\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: apps\monitor
Source: "{#SourcePath}\Web\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: apps\web
Source: "{#SourcePath}\CLI\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: apps\cli

[Icons]
Name: "{group}\Applications\AI Usage Tracker UI"; Filename: "{app}\AIUsageTracker.exe"; Components: apps\tracker
Name: "{group}\Applications\AI Usage Tracker Monitor"; Filename: "{app}\AIUsageTracker.Monitor.exe"; Components: apps\monitor
Name: "{group}\Applications\AI Usage Tracker Web UI"; Filename: "{app}\AIUsageTracker.Web.exe"; Components: apps\web
Name: "{group}\Applications\AI Usage Tracker CLI"; Filename: "{app}\AIUsageTracker.CLI.exe"; Components: apps\cli
Name: "{group}\{cm:UninstallProgram,AI Usage Tracker}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\AI Usage Tracker"; Filename: "{app}\AIUsageTracker.exe"; Tasks: desktopicontracker; Components: apps\tracker

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AI Usage Tracker Monitor"; ValueData: """{app}\AIUsageTracker.Monitor.exe"""; Tasks: startupmonitor; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AI Usage Tracker"; ValueData: """{app}\AIUsageTracker.exe"""; Tasks: startuptracker; Flags: uninsdeletevalue

[Run]
Filename: "{app}\AIUsageTracker.exe"; Description: "{cm:LaunchProgram,AI Usage Tracker UI}"; Flags: nowait postinstall skipifsilent; Components: apps\tracker; Check: ShouldRunApplication
