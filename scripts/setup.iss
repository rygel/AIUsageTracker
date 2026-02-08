; AI Consumption Tracker - Inno Setup Script

#ifndef MyAppVersion
  #define MyAppVersion "1.7.8"
#endif
#ifndef SourcePath
  #define SourcePath "..\dist\publish-win-x64"
#endif
#ifndef MyAppArch
  #define MyAppArch "x64"
#endif

[Code]
function InitializeSetup(): Boolean;
begin
  // Validate architecture match
  if MyAppArch = "x64" then
    if not IsWin64 or ProcessorArchitecture <> "x64" then
      MsgBox('Error: Attempting to install x64 installer on ' + ProcessorArchitecture + ' system.' + #13 + 'Please download the correct installer.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end else if MyAppArch = "x86" then
    if not (ProcessorArchitecture = "x86" or ProcessorArchitecture = "arm") then
      MsgBox('Error: Attempting to install x86 installer on ' + ProcessorArchitecture + ' system.' + #13 + 'Please download the correct installer.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end else if MyAppArch = "arm64" then
    if ProcessorArchitecture <> "arm64" then
      MsgBox('Error: Attempting to install arm64 installer on ' + ProcessorArchitecture + ' system.' + #13 + 'Please download the correct installer.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  Result := True;
end;

function NextButtonClick(CurPage: Integer): Boolean;
begin
  Result := InitializeSetup();
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
