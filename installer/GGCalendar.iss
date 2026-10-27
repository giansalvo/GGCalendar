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
; The setup always asks which language to use (also when upgrading). The GPL v3 is shown for information, in that language (GPL.<language>.txt are
; unofficial translations; English is the original, LICENSE), because the license does not require accepting it to install or run the program.
ShowLanguageDialog=yes
UsePreviousLanguage=no
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
Name: "english"; MessagesFile: "compiler:Default.isl"; InfoBeforeFile: "..\LICENSE"; InfoAfterFile: "Novita.en.txt"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"; InfoBeforeFile: "GPL.it.txt"; InfoAfterFile: "Novita.it.txt"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"; InfoBeforeFile: "GPL.de.txt"; InfoAfterFile: "Novita.de.txt"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"; InfoBeforeFile: "GPL.es.txt"; InfoAfterFile: "Novita.es.txt"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"; InfoBeforeFile: "GPL.fr.txt"; InfoAfterFile: "Novita.fr.txt"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Novita.*.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "GPL.*.txt"; DestDir: "{app}"; Flags: ignoreversion

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

; The data of the user in %AppData%\GGCalendar (calendar, settings, Google accounts) is deleted on uninstall if the user says so (always in a silent uninstall).
[CustomMessages]
english.DeleteUserData=Do you also want to delete your GGCalendar data on this computer (calendars, events, settings and connected Google accounts)?%n%nThis cannot be undone. The events in your Google account are not deleted.
italian.DeleteUserData=Vuoi eliminare anche i dati di GGCalendar presenti su questo computer (calendari, eventi, impostazioni e account Google collegati)?%n%nL'operazione non si può annullare. Gli eventi nel tuo account Google non vengono eliminati.
german.DeleteUserData=Möchten Sie auch die GGCalendar-Daten auf diesem Computer löschen (Kalender, Termine, Einstellungen und verbundene Google-Konten)?%n%nDies kann nicht rückgängig gemacht werden. Die Termine in Ihrem Google-Konto werden nicht gelöscht.
spanish.DeleteUserData=¿Quieres eliminar también los datos de GGCalendar de este equipo (calendarios, eventos, ajustes y cuentas de Google conectadas)?%n%nNo se puede deshacer. Los eventos de tu cuenta de Google no se eliminan.
french.DeleteUserData=Voulez-vous aussi supprimer les données de GGCalendar sur cet ordinateur (agendas, événements, paramètres et comptes Google connectés) ?%n%nCette action est irréversible. Les événements de votre compte Google ne sont pas supprimés.

[Code]
// The data of the user is deleted: always in a silent uninstall, only if the user says so in an interactive one (default: no).
// An upgrade does not uninstall anything, so it keeps the data.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Delete: Boolean;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  if UninstallSilent then
    Delete := True
  else
    Delete := MsgBox(CustomMessage('DeleteUserData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  if Delete then
    DelTree(ExpandConstant('{userappdata}\GGCalendar'), True, True, True);
end;
