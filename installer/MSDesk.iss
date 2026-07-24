; Inno-Setup-Skript fuer MSDesk. Version wird per /DAppVersion=x.y.z uebergeben.
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{7E2B9C14-4F3A-4C8E-9D21-MSDESK000002}
AppName=MSDesk
AppVersion={#AppVersion}
AppPublisher=ISD Michael Sabitzer
DefaultDirName={autopf}\MSDesk
DefaultGroupName=MSDesk
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\MSDesk.exe
OutputDir=..\dist
OutputBaseFilename=MSDesk-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
CloseApplications=yes
RestartApplications=no
PrivilegesRequired=admin
SetupIconFile=..\src\MSDesk\Assets\MSDesk.ico

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
; Nur bei der ERSTEN Installation anbieten. Bei Updates wuerde sonst jedes Mal
; eine neue Desktop-Verknuepfung entstehen — die der Nutzer laengst in einen
; Bereich einsortiert hat.
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen"; GroupDescription: "Zusätzliche Symbole:"; \
    Check: IstNeuinstallation

[Files]
Source: "..\publish\MSDesk.exe"; DestDir: "{app}"; Flags: ignoreversion
; Symbol-Galerie: liegt bei PublishSingleFile NEBEN der EXE und muss mitinstalliert
; werden — sonst zeigen Bereiche und Tabs keine Symbole.
Source: "..\publish\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs
; Sicherheitsnetz: sollte ein Build die nativen WPF-Bibliotheken doch neben die
; EXE legen statt sie einzubetten, werden sie mitinstalliert — ohne sie startet
; die Anwendung nicht.
Source: "..\publish\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\MSDesk"; Filename: "{app}\MSDesk.exe"
Name: "{autodesktop}\MSDesk"; Filename: "{app}\MSDesk.exe"; Tasks: desktopicon; Check: IstNeuinstallation

; Hinweis: Den Autostart richtet MSDesk beim ersten Start selbst ein (HKCU des
; echten Anwenders, nicht des ggf. erhoehten Installer-Kontexts). Im Tray abschaltbar.

[Run]
; Ohne "skipifsilent": auch das stille Update (aus dem Programm heraus) startet
; MSDesk danach wieder. "runasoriginaluser" ist wichtig — sonst liefe MSDesk
; erhoeht weiter und koennte keine Dateien mehr aus dem Explorer annehmen.
Filename: "{app}\MSDesk.exe"; Description: "MSDesk jetzt starten"; \
    Flags: nowait postinstall runasoriginaluser

[UninstallRun]
; Vor dem Entfernen fragen, ob die Inhalte der Bereiche zurueck auf den Desktop
; sollen — sonst blieben die Verknuepfungen unsichtbar in den Ordnern liegen.
Filename: "{app}\MSDesk.exe"; Parameters: "--icons-auf-desktop"; RunOnceId: "IconsAufDesktop"; \
    Flags: waituntilterminated

[Code]
{ True, solange MSDesk noch NICHT installiert ist. Steuert, dass die
  Desktop-Verknuepfung nur einmal angelegt wird und Updates den Desktop
  in Ruhe lassen. }
function IstNeuinstallation(): Boolean;
var
  vorhanden: String;
begin
  Result := not (
    RegQueryStringValue(HKLM,
      'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7E2B9C14-4F3A-4C8E-9D21-MSDESK000002}_is1',
      'UninstallString', vorhanden)
    or RegQueryStringValue(HKCU,
      'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7E2B9C14-4F3A-4C8E-9D21-MSDESK000002}_is1',
      'UninstallString', vorhanden));
end;

{ Die Vorgaengerversion hiess "ISDesk" und liegt in einem eigenen Ordner.
  Sie wird vor der Installation still entfernt, damit nicht zwei Programme
  nebeneinander laufen. Einstellungen bleiben erhalten — MSDesk uebernimmt
  sie beim ersten Start aus %APPDATA%\ISDesk. }
function GetLegacyUninstaller(): String;
var
  key, path: String;
begin
  Result := '';
  key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7E2B9C14-4F3A-4C8E-9D21-ISDESK000001}_is1';
  if RegQueryStringValue(HKLM, key, 'UninstallString', path)
     or RegQueryStringValue(HKCU, key, 'UninstallString', path) then
    Result := RemoveQuotes(path);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  uninstaller: String;
  code: Integer;
begin
  Result := '';
  uninstaller := GetLegacyUninstaller();
  if (uninstaller <> '') and FileExists(uninstaller) then
    Exec(uninstaller, '/VERYSILENT /NORESTART /SUPPRESSMSGBOXES', '',
         SW_HIDE, ewWaitUntilTerminated, code);
end;
