; Inno Setup script. Built by .github/workflows/release.yml:
;   ISCC.exe /DAppVersion=0.1.2 /DSourceDir=<folder with the two exe files> installer\MasterHub.iss
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
AppId={{6F0B1B7B-5E0C-4C57-9B52-7A4D3E2C9A11}
AppName=MasterHub
AppVersion={#AppVersion}
AppPublisher=Dreyark
AppPublisherURL=https://github.com/Dreyark/MasterHub
AppSupportURL=https://github.com/Dreyark/MasterHub/issues
DefaultDirName={autopf}\MasterHub
DefaultGroupName=MasterHub
DisableProgramGroupPage=yes
; Installs for the current user only (no administrator prompt); the person can pick "all users" in the dialog.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\MasterHub.Avalonia.exe
OutputDir=..\installer-out
OutputBaseFilename=MasterHub-Setup-{#AppVersion}

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceDir}\MasterHub.Avalonia.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\masterhub-cli.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\MasterHub"; Filename: "{app}\MasterHub.Avalonia.exe"
Name: "{autodesktop}\MasterHub"; Filename: "{app}\MasterHub.Avalonia.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MasterHub.Avalonia.exe"; Description: "Start MasterHub"; Flags: nowait postinstall skipifsilent

; Uninstalling removes only the program files. Profiles and settings stay in %APPDATA%\MasterHub.
