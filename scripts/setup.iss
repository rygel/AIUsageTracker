; AI Usage Tracker - Inno Setup Script

#ifndef MyAppVersion
  #define MyAppVersion "2.2.26"
#endif
#ifndef SourcePath
  #define SourcePath "..\dist\publish-win-x64"
#endif
#ifndef MyAppArch
  #define MyAppArch "x64"
#endif

[Code]
var
  RestartApplications: Boolean;
  DeleteDatabase: Boolean;

function InitializeSetup(): Boolean;
var
  Arch: String;
  i: Integer;
  Param: String;
begin
  Result := True;
  Arch := '{#MyAppArch}';
  RestartApplications := False;

  // Check for /RESTARTAPPLICATIONS parameter
  for i := 1 to ParamCount do
  begin
    Param := ParamStr(i);
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
AppPublisherURL=https://github.com/rygel/AIConsumptionTracker
AppSupportURL=https://github.com/rygel/AIConsumptionTracker
AppUpdatesURL=https://github.com/rygel/AIConsumptionTracker/releases
AlwaysShowComponentsList=yes
DefaultDirName={autopf}\AIUsageTracker
DefaultGroupName=AI Usage Tracker
OutputDir=..\dist
OutputBaseFilename=AIUsageTracker_Setup_v{#MyAppVersion}_{#MyAppArch}
Compression=lzma
SolidCompression=yes
CloseApplications=yes
DisableDirPage=auto
DirExistsWarning=no
SetupIconFile=..\AIUsageTracker.UI.Slim\Assets\app_icon.ico
UninstallDisplayIcon={app}\AIUsageTracker.exe
PrivilegesRequired=lowest

#if MyAppArch == "x64"
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
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

[Files]
Source: "..\AIUsageTracker.UI.Slim\Assets\app_icon.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\README.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourcePath}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
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
Name: "{userstartup}\AI Usage Tracker Monitor"; Filename: "{app}\AIUsageTracker.Monitor.exe"; Tasks: startupmonitor; Components: apps\monitor

[Run]
Filename: "{app}\AIUsageTracker.exe"; Description: "{cm:LaunchProgram,AI Usage Tracker UI}"; Flags: nowait postinstall skipifsilent; Components: apps\tracker; Check: ShouldRunApplication


