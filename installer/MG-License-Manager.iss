#define MyAppName "MG Business Platform License Manager"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "MG"
#define MyAppExeName "MG.BusinessPlatform.LicenseManager.exe"

[Setup]
AppId={{C3C2A0B4-4B5F-48D2-9E52-2D7B37A88D4C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\MG Business Platform License Manager
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=MG-License-Manager-Setup-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\artifacts\publish\license-tool\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\docs\LICENSE_MANAGER.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autoprograms}\{#MyAppName} - Command line"; Filename: "{app}\MG.BusinessPlatform.LicenseTool.exe"; Parameters: "help"; WorkingDir: "{app}"
Name: "{autoprograms}\{#MyAppName} - Documentation"; Filename: "{app}\LICENSE_MANAGER.md"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: postinstall nowait skipifsilent

[UninstallDelete]
; Signing-key files and license data are intentionally not stored or deleted by the installer.
