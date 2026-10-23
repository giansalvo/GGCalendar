; Inno Setup script for GGCalendar. Build with installer\build-installer.ps1, which publishes the app first.

; AppVersion comes from <Version> in GGCalendar.App.csproj (passed by build-installer.ps1 with /DAppVersion=).
#ifndef AppVersion
  #error AppVersion non definita: compila con installer\build-installer.ps1.
#endif
#define AppExe "GGCalendar.App.exe"

[Setup]
; AppId identifies the installation for upgrades and uninstall: never change it.
AppId={{037D6627-E8E5-4A43-AA0D-AE5656F6ED8C}
AppName=GGCalendar
AppVersion={#AppVersion}
AppVerName=GGCalendar {#AppVersion}
AppPublisher=Giansalvo Gusinu
AppCopyright=© 2026 Giansalvo Gusinu
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\GGCalendar
DefaultGroupName=GGCalendar
DisableProgramGroupPage=yes
; Installs for the current user without admin rights; the user can choose "all users" in the first page.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 11 (build 22000) or later.
MinVersion=10.0.22000
OutputDir=..\website\downloads
OutputBaseFilename=GGCalendar-v{#AppVersion}-Setup
; GPL v3 shown for information: the license does not require accepting it to install or run the program.
InfoBeforeFile=..\LICENSE
; What is new in this version, shown at the end of the setup and kept in the program folder.
InfoAfterFile=Novita.txt
SetupIconFile=..\src\GGCalendar.App\Assets\GGCalendar.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=GGCalendar
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Novita.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\GGCalendar"; Filename: "{app}\{#AppExe}"
Name: "{autoprograms}\GGCalendar - Novita"; Filename: "{app}\Novita.txt"
Name: "{autodesktop}\GGCalendar"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,GGCalendar}"; Flags: nowait postinstall skipifsilent

; User data in %AppData%\GGCalendar (calendar, settings, Google accounts) is kept on uninstall.
