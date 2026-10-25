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
; What is new in this version is shown at the end of the setup: the file is chosen per language, in [Languages].
SetupIconFile=..\src\GGCalendar.App\Assets\GGCalendar.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=GGCalendar
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

; The setup speaks the language of Windows when it is one of these (else English); the user can choose another one.
; Each language has its own what-is-new text, shown at the end of the setup and kept in the program folder.
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"; InfoAfterFile: "Novita.en.txt"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"; InfoAfterFile: "Novita.it.txt"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"; InfoAfterFile: "Novita.de.txt"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"; InfoAfterFile: "Novita.es.txt"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"; InfoAfterFile: "Novita.fr.txt"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Novita.*.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\GGCalendar"; Filename: "{app}\{#AppExe}"
Name: "{autoprograms}\GGCalendar - What's new"; Filename: "{app}\Novita.en.txt"; Languages: english
Name: "{autoprograms}\GGCalendar - Novità"; Filename: "{app}\Novita.it.txt"; Languages: italian
Name: "{autoprograms}\GGCalendar - Neuigkeiten"; Filename: "{app}\Novita.de.txt"; Languages: german
Name: "{autoprograms}\GGCalendar - Novedades"; Filename: "{app}\Novita.es.txt"; Languages: spanish
Name: "{autoprograms}\GGCalendar - Nouveautés"; Filename: "{app}\Novita.fr.txt"; Languages: french
Name: "{autodesktop}\GGCalendar"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,GGCalendar}"; Flags: nowait postinstall skipifsilent

; User data in %AppData%\GGCalendar (calendar, settings, Google accounts) is kept on uninstall.
