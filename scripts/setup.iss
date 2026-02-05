; AI Consumption Tracker - Inno Setup Script

#define MyAppVersion "1.2.0"
#ifndef SourcePath
  #define SourcePath "..\dist\publish-win-x64"
#endif

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
; SetupIconFile=..\AIConsumptionTracker.UI\Assets\app_icon.ico
UninstallDisplayIcon={app}\AIConsumptionTracker.UI.exe
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

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
