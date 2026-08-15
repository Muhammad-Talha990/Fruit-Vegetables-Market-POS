; ==========================================
; PMC — Pak Madinah Commission Agents
; Fruit & Vegetable Market POS Installer
; Target: .NET 8 Windows x64 (self-contained)
; ==========================================

#define MyAppName "PMC - Pak Madinah Commission Agents"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Weblynx Hive"
#define MyAppURL "https://github.com/Muhammad-Talha990/Fruit-Vegetables-Market-POS"
#define MyAppExeName "FruitVegetableMarketPOS.exe"
#define PublishDir "..\bin\Release\net8.0-windows\win-x64\publish"
#define MyAppDataFolder "FruitVegetableMarketPOS"

[Setup]

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
OutputBaseFilename=PMC_POS_Setup_v{#MyAppVersion}

Compression=lzma2
SolidCompression=yes

WizardStyle=modern

CloseApplications=yes
RestartApplications=yes

LicenseFile=..\LICENSE

UninstallDisplayIcon={app}\{#MyAppExeName}

VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=PMC - Pak Madinah Commission Agents POS
VersionInfoCopyright=Copyright (C) 2026 Weblynx Hive

DisableDirPage=no
DisableReadyMemo=no

[Languages]

Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]

Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]

; Published app binaries + Assets\Products images
Source: "{#PublishDir}\*"; Excludes: "*.db,*.db-shm,*.db-wal,printer_config.txt"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

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
; User database is kept in %LOCALAPPDATA%\FruitVegetableMarketPOS
; Uncomment the next line only if uninstall should also wipe that data:
; Type: filesandordirs; Name: "{localappdata}\{#MyAppDataFolder}"
