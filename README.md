# MSDesk

Schlanker Desktop-Organizer für Windows 11: Bereiche auf dem Desktop, in denen
Verknüpfungen und Dateien nach Themen zusammenliegen — immer hinter den normalen
Fenstern. Jeder Bereich kann mehrere Tabs haben, hinter jedem Tab steckt ein
echter Ordner auf der Platte.

Eigenentwicklung als Ersatz für Stardock Fences, ohne Lizenzierung, Telemetrie
und Explorer-Eingriffe. Windows-only (WPF, .NET 8), Tray-Symbol ohne Hauptfenster.
(Bis Version 0.19 hieß die Anwendung *ISDesk*.)

## Installieren

Aktuellen Installer aus den [Releases](https://github.com/sabitzerm-isd/isdesk/releases/latest)
laden und ausführen — installiert nach `C:\Program Files\MSDesk`, legt einen
Startmenü-Eintrag an und startet künftig mit Windows. .NET wird nicht benötigt.

Windows meldet „Unbekannter Herausgeber" (die Anwendung ist nicht signiert):
*Weitere Informationen* → *Trotzdem ausführen*.

Läuft MSDesk bereits, geht es bequemer: **Optionen → Allgemein → Update →
Nach Updates suchen → Update installieren**.

## Funktionen

- Bereiche mit Tabs, Transparenz und Milchglas-Effekt, Symbolen und Tab-Farben
- Icons frei anordnen, Drag & Drop aus Explorer und Browser, echtes
  Windows-Rechtsklickmenü, Papierkorb und andere Systemobjekte
- Live-Suche über alle Bereiche, Raster und Einrasten an Nachbarn
- Ablage: sammelt den Desktop auf Knopfdruck ein, Regeln je Dateiendung
  (`sza`, `ifc` … oder Sammelbegriffe `bilder`, `office`, `video`, `audio`, `archiv`),
  merkt sich, wo eine Datei zuletzt lag
- Lesezeichen aus Chrome und Firefox importieren und abgleichen
- Sicherung als ZIP (behält die neuesten drei), Layouts je Bildschirm-Konfiguration
- Automatische Update-Prüfung über GitHub Releases
- Beim Deinstallieren werden die Inhalte auf Wunsch zurück auf den Desktop gelegt

Eine ausführliche Anleitung öffnet sich beim ersten Start und ist danach über
das Tray-Symbol → *Anleitung öffnen* erreichbar.

## Entwicklung

```bash
dotnet build MSDesk.sln
dotnet test --nologo
```

Release bauen (Installer landet in `dist/`):

```bash
dotnet publish src/MSDesk/MSDesk.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Danach `installer/MSDesk.iss` mit Inno Setup übersetzen (`/DAppVersion=x.y.z`).

Einstellungen liegen unter `%APPDATA%\MSDesk\config.json`, die Bereichs-Ordner
standardmäßig unter `D:\Fences`.
