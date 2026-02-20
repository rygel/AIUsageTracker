; AI Consumption Tracker - Inno Setup Script

#ifndef MyAppVersion
  #define MyAppVersion "2.0.2"
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
  // Run if /RESTARTAPPLICATIONS was passed OR if user checked the box (not in silent mode)
  Result := RestartApplications;
end;

[Setup]
AppId={{D3B3E8A1-8E9D-4F6B-A2B3-7C8D9E0F1A2B}
AppName=AI Consumption Tracker
AppVersion={#MyAppVersion}
AppPublisher=Alexander Brandt
DefaultDirName={autopf}\AIConsumptionTracker
DefaultGroupName=AI Consumption Tracker
OutputDir=..\dist
OutputBaseFilename=AIConsumptionTracker_Setup_v{#MyAppVersion}_{#MyAppArch}
Compression=lzma
SolidCompression=yes
CloseApplications=yes
DisableDirPage=auto
DirExistsWarning=no
SetupIconFile=..\AIConsumptionTracker.UI\Assets\app_icon.ico
UninstallDisplayIcon={app}\app_icon.ico
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
Name: "apps\tracker"; Description: "Tracker (Slim UI)"; Types: full compact custom
Name: "apps\ui"; Description: "Classic UI"; Types: full custom
Name: "apps\agent"; Description: "Agent"; Types: full custom
Name: "apps\web"; Description: "Web UI"; Types: full custom
Name: "apps\cli"; Description: "CLI"; Types: full compact custom

[Tasks]
Name: "desktopicontracker"; Description: "Create Tracker desktop icon"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; Components: apps\tracker
Name: "startuptracker"; Description: "Run Tracker at Windows Startup"; GroupDescription: "Additional options:"; Flags: unchecked; Components: apps\tracker

[Files]
Source: "..\AIConsumptionTracker.UI\Assets\app_icon.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\README.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourcePath}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourcePath}\Tracker\*"; DestDir: "{app}\Tracker"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: apps\tracker
Source: "{#SourcePath}\UI\*"; DestDir: "{app}\UI"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: apps\ui
Source: "{#SourcePath}\Agent\*"; DestDir: "{app}\Agent"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: apps\agent
Source: "{#SourcePath}\Web\*"; DestDir: "{app}\Web"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: apps\web
Source: "{#SourcePath}\CLI\*"; DestDir: "{app}\CLI"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: apps\cli

[Icons]
Name: "{group}\Tracker (Slim UI)"; Filename: "{app}\Tracker\AIConsumptionTracker.exe"; Components: apps\tracker
Name: "{group}\Classic UI"; Filename: "{app}\UI\AIConsumptionTracker.UI.exe"; Components: apps\ui
Name: "{group}\Agent"; Filename: "{app}\Agent\AIConsumptionTracker.Agent.exe"; Components: apps\agent
Name: "{group}\Web UI"; Filename: "{app}\Web\AIConsumptionTracker.Web.exe"; Components: apps\web
Name: "{group}\CLI"; Filename: "{app}\CLI\AIConsumptionTracker.CLI.exe"; Components: apps\cli
Name: "{group}\{cm:UninstallProgram,AI Consumption Tracker}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\AI Consumption Tracker"; Filename: "{app}\Tracker\AIConsumptionTracker.exe"; Tasks: desktopicontracker; Components: apps\tracker
Name: "{userstartup}\AI Consumption Tracker"; Filename: "{app}\Tracker\AIConsumptionTracker.exe"; Tasks: startuptracker; Components: apps\tracker

[Run]
Filename: "{app}\Tracker\AIConsumptionTracker.exe"; Description: "{cm:LaunchProgram,AI Consumption Tracker}"; Flags: nowait postinstall skipifsilent; Components: apps\tracker; Check: ShouldRunApplication

