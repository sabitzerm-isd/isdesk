; Inno-Setup-Skript fuer ISDesk. Version wird per /DAppVersion=x.y.z uebergeben.
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{7E2B9C14-4F3A-4C8E-9D21-ISDESK000001}
AppName=ISDesk
AppVersion={#AppVersion}
AppPublisher=ISD Michael Sabitzer
DefaultDirName={autopf}\ISDesk
DefaultGroupName=ISDesk
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\ISDesk.exe
OutputDir=..\dist
OutputBaseFilename=ISDesk-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
CloseApplications=yes
RestartApplications=no
PrivilegesRequired=admin
SetupIconFile=..\src\ISDesk\Assets\ISDesk.ico

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen"; GroupDescription: "Zusätzliche Symbole:"

[Files]
Source: "..\publish\ISDesk.exe"; DestDir: "{app}"; Flags: ignoreversion
; Symbol-Galerie: liegt bei PublishSingleFile NEBEN der EXE und muss mitinstalliert
; werden — sonst zeigen Bereiche und Tabs keine Symbole.
Source: "..\publish\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ISDesk"; Filename: "{app}\ISDesk.exe"
Name: "{autodesktop}\ISDesk"; Filename: "{app}\ISDesk.exe"; Tasks: desktopicon

; Hinweis: Den Autostart richtet ISDesk beim ersten Start selbst ein (HKCU des
; echten Anwenders, nicht des ggf. erhoehten Installer-Kontexts). Im Tray abschaltbar.

[Run]
Filename: "{app}\ISDesk.exe"; Description: "ISDesk jetzt starten"; Flags: nowait postinstall skipifsilent
