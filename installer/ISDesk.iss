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
Name: "autostart"; Description: "ISDesk automatisch mit Windows starten"; GroupDescription: "Start:"

[Files]
Source: "..\publish\ISDesk.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\ISDesk"; Filename: "{app}\ISDesk.exe"
Name: "{userdesktop}\ISDesk"; Filename: "{app}\ISDesk.exe"; Tasks: autostart

[Registry]
; Autostart (optional, ueber Task): HKCU-Run-Eintrag
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "ISDesk"; ValueData: """{app}\ISDesk.exe"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\ISDesk.exe"; Description: "ISDesk jetzt starten"; Flags: nowait postinstall skipifsilent
