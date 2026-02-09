; AI Consumption Tracker - Inno Setup Script

#ifndef MyAppVersion
  #define MyAppVersion "1.7.14"
#endif
#ifndef SourcePath
  #define SourcePath "..\dist\publish-win-x64"
#endif
#ifndef MyAppArch
  #define MyAppArch "x64"
#endif

[Code]
function InitializeSetup(): Boolean;
var
  Arch: String;
begin
  Result := True;
  Arch := '{#MyAppArch}';

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

[Setup]
AppId={{D3B3E8A1-8E9D-4F6B-A2B3-7C8D9E0F1A2B}
AppName=AI Consumption Tracker
AppVersion={#MyAppVersion}
AppPublisher=Alexander Brandt
DefaultDirName={autopf}\AIConsumptionTracker
DefaultGroupName=AI Consumption Tracker
OutputDir=..\dist
OutputBaseFilename=AIConsumptionTracker_Setup_v{#MyAppVersion}
Compression=lzma
SolidCompression=yes
CloseApplications=yes
DisableDirPage=auto
DirExistsWarning=no
SetupIconFile=..\AIConsumptionTracker.UI\Assets\app_icon.ico
UninstallDisplayIcon={app}\AIConsumptionTracker.UI.exe
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

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Run at Windows Startup"; GroupDescription: "Additional options:"; Flags: unchecked

[Files]
Source: "{#SourcePath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\AI Consumption Tracker"; Filename: "{app}\AIConsumptionTracker.UI.exe"
Name: "{group}\{cm:UninstallProgram,AI Consumption Tracker}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\AI Consumption Tracker"; Filename: "{app}\AIConsumptionTracker.UI.exe"; Tasks: desktopicon
Name: "{userstartup}\AI Consumption Tracker"; Filename: "{app}\AIConsumptionTracker.UI.exe"; Tasks: startup

[Run]
Filename: "{app}\AIConsumptionTracker.UI.exe"; Description: "{cm:LaunchProgram,AI Consumption Tracker}"; Flags: nowait postinstall skipifsilent

