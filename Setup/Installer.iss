; ==========================================
; Grocery POS Installer
; Created for: .NET 8 (Win-x64)
; ==========================================

#define MyAppName "GroceryPOS"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Weblynx Hive"
#define MyAppURL "https://weblynx-hive.onrender.com/"
#define MyAppExeName "GroceryPOS.exe"
#define PublishDir "..\bin\Release\net8.0-windows\win-x64\publish"

[Setup]

; Generate your own GUID if publishing publicly
AppId={{C6B5E86E-A79B-4D14-9BCA-8472B702D8C9}}

AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}

AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}

DisableProgramGroupPage=yes

PrivilegesRequired=admin

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir=.\Releases
OutputBaseFilename=GroceryPOS_Setup_v{#MyAppVersion}

SetupIconFile=..\Assets\logo.ico

Compression=lzma2
SolidCompression=yes

WizardStyle=modern

CloseApplications=yes
RestartApplications=yes

LicenseFile=..\LICENSE

UninstallDisplayIcon={app}\{#MyAppExeName}

VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Grocery POS System
VersionInfoCopyright=© Weblynx Hive

DisableDirPage=no
DisableReadyMemo=no

[Languages]

Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]

Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]

Source: "{#PublishDir}\*"; Excludes: "*.db"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]

Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]

Filename: "{app}\{#MyAppExeName}"; \
Description: "{cm:LaunchProgram,{#StringChange(MyAppName,'&','&&')}}"; \
Flags: nowait postinstall skipifsilent

[UninstallDelete]

Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{localappdata}\{#MyAppName}"