# ISDesk

ISDesk ist ein schlanker Ersatz fuer Stardock Fences: verschiebbare, transparente
Desktop-Bereiche ("Fences"), die Verknuepfungen und Dateien nach Themen gruppieren.
Jeder Bereich liegt dauerhaft hinter den normalen Fenstern und spiegelt echte Ordner
auf der Platte.

Windows-only (WPF, .NET 8). Reine Desktop-App mit Tray-Symbol, ohne Hauptfenster.

## Features (Phase 1)

- **Desktop-Bereiche** – randlose Fenster mit Acrylic-Transparenz und runden Ecken,
  dauerhaft "bottom-most" (nie im Vordergrund, nicht in Alt-Tab oder Taskleiste).
- **Verschieben & Groesse aendern** – Ziehen an der Titelzeile, Anfassen der Raender.
- **Tabs** – je Tab ein echter Ordner; anlegen, wechseln, umbenennen, entfernen
  (der Ordner bleibt beim Entfernen erhalten).
- **Icon-Raster** – Shell-Icons wie im Explorer, Start per Doppelklick, Live-Aktualisierung
  ueber `FileSystemWatcher`.
- **Drag & Drop** – Dateien in einen Bereich ziehen (verschieben, mit Strg kopieren) und
  wieder heraus auf den Desktop oder in andere Ordner.
- **Icon-Kontextmenue** – Oeffnen, im Explorer anzeigen, umbenennen, in den Papierkorb.
- **Bereichs-Kontextmenue** – neuer Tab, umbenennen, Transparenz-Regler (live),
  Hintergrund-Blur, Ordner oeffnen, neuer/entfernen Bereich, beenden.
- **Tray-Menue** – neuer Bereich, alle Bereiche neu ausrichten, Autostart-Umschalter, beenden.
- **Persistenz** – Layout, Groesse, Transparenz und Tabs werden als JSON gespeichert
  (atomar, entprellt). Bereiche ausserhalb sichtbarer Bildschirme werden zurueckgeholt.
- **Erststart-Demo** – beim ersten Start entsteht ein Bereich "Willkommen" mit ein paar
  Beispiel-Verknuepfungen. Bestehende Nutzerdateien werden dabei nie angefasst.
- **Single-Instance & Autostart** – nur eine Instanz laeuft; Autostart optional per
  Registry-Eintrag (`HKCU\...\Run`).

## Voraussetzungen

- Windows 10/11 (x64)
- .NET SDK 8.0

## Build

```
dotnet build ISDesk.sln
```

Release-Build:

```
dotnet build ISDesk.sln -c Release
```

Tests (Logik: ConfigService, FolderContents):

```
dotnet test ISDesk.sln
```

## Start

Nach dem Build die erzeugte EXE starten:

```
src\ISDesk\bin\Debug\net8.0-windows\ISDesk.exe
```

oder direkt:

```
dotnet run --project src\ISDesk
```

Die App erscheint als Tray-Symbol. Ueber das Tray-Menue oder das Kontextmenue eines
Bereichs lassen sich neue Bereiche anlegen.

## Konfiguration

- Konfigurationsdatei: `%APPDATA%\ISDesk\config.json`
- Standard-Basisordner fuer Bereiche/Tabs: `D:\Fences` (aenderbar ueber `BaseFolder`
  in der `config.json`)
- Bei defekter Konfiguration wird eine Sicherung als `config.bad.json` abgelegt und mit
  Standardwerten weitergearbeitet.
- Unbehandelte Ausnahmen landen in `%APPDATA%\ISDesk\crash.log`.

## Bekannte Einschraenkungen

- **Win+D / "Desktop anzeigen"** blendet die Bereiche mit aus – ein Ausblende-Schutz ist
  fuer Phase 2 geplant.
- **Update-Mechanik** (Auto-Update / Installer) folgt in Phase 1d.
- Keine Ordner-Navigation innerhalb eines Tabs und (noch) keine Einstellungen-Oberflaeche
  (Icon-Groesse etc. nur ueber die `config.json`).

## Projektstruktur

```
src/ISDesk/            WPF-App (Views, ViewModels, Services, Interop)
tests/ISDesk.Tests/    xunit-Tests (ConfigService, FolderContents)
```
