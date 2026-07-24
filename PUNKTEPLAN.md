# Punkteplan: ISDesk — eigener Fences-Nachbau

Stand: 23.07.2026 · Status: **Kernentscheidungen beschlossen** (siehe Abschnitt 5)

## 0. Ziel & Rahmen

Eine schlanke, moderne Eigenentwicklung, die die von dir genutzten Fences-Funktionen abdeckt,
ohne den Ballast (Lizenzierung, Telemetrie, Skins, Regeln, Desktop Pages). Verteilbar an
Kollegen per Installer, Updates automatisch über GitHub Releases.

**Wichtig (Recht/Marke):** Wir bauen die *Funktionalität* eigenständig nach — kein Code, keine
Grafiken, keine Namen von Stardock. „Fences" ist ein Produktname von Stardock, die App bekommt
einen eigenen Namen. Funktionsnachbau in eigener Implementierung ist zulässig.

## 1. Punkteplan — Was rein kommt und was nicht

### A. MUSS — Phase 1 (MVP, deine Vorgaben)

| # | Punkt | Detail |
|---|---|---|
| A1 | Bereiche (Fences) erstellen | Rechtsklick auf Desktop-Bereich der App / Tray-Menü → „Neuer Bereich"; Titel, Position, Größe frei; verschieben/ändern per Maus |
| A2 | Ordner-Anbindung | Jeder Bereich/Tab zeigt einen echten Ordner (Standard: `D:\Fences\<Name>`, anpassbar; bestehende Ordner verknüpfbar). Neuer Bereich = Ordner wird automatisch angelegt |
| A3 | Icons hinpacken | Drag & Drop vom Desktop/Explorer in den Bereich (Verschieben oder Verknüpfung — einstellbar), Doppelklick startet, Drag heraus möglich, Basis-Kontextmenü (Öffnen, Umbenennen, Löschen, Im Explorer zeigen) |
| A4 | Transparenz ab Tag 1 | Regler pro Bereich (0–100 %) + globaler Standardwert; Hintergrund-Blur (Acrylic) zuschaltbar |
| A5 | Tabs ab Tag 1 | Mehrere Tabs pro Fenster, jeder Tab = eigener Ordner; Tabs anlegen/umbenennen/umsortieren/schließen |
| A6 | Moderne Optik | Windows-11-Look: Acrylic/Blur, runde Ecken, Dark/Light dem System folgend, Akzentfarbe, dezente Animationen — kein Windows-95-Charme |
| A7 | Desktop-Verhalten | Fenster liegen immer HINTER normalen Apps (bottom-most), tauchen nicht in Alt-Tab auf, überleben „Desktop anzeigen" |
| A8 | GitHub ab Tag 1 | Repo mit komplettem Quellcode, sauberer Commit-Historie |
| A9 | Auto-Update ab Tag 1 | App prüft beim Start GitHub Releases; neue Version → Banner „Update verfügbar" → Download + Installer startet (bewährtes Muster aus unseren anderen Apps vorhanden) |
| A10 | Grundgerüst | Tray-Icon (Beenden, Einstellungen, Neuer Bereich), Autostart mit Windows, Konfiguration als JSON in `%APPDATA%`, läuft ohne Adminrechte |

### B. SOLL — Phase 2 (aus deiner echten Nutzung abgeleitet)

| # | Punkt | Warum |
|---|---|---|
| B1 | Ordner-Navigation in der Fence | Nutzt du heute intensiv (PM, Inkremente, Kunden …): Doppelklick auf Unterordner → Inhalt im Bereich, Breadcrumb zurück |
| B2 | Roll-up | Nutzt du heute (ISD-Fence): Doppelklick auf Titel → nur Titelleiste |
| B3 | Icon-Größe & Sortierung pro Tab | Nutzt du heute (16–96 px je Ordner) |
| B4 | Layout-Snapshots | Automatische tägliche Sicherung + „Layout wiederherstellen" (macht Fences heute für dich) |
| B5 | Multi-Monitor robust | Docking/RDP/Auflösungswechsel: Bereiche bleiben wo sie hingehören (Fences' größte Schwäche laut deinen Logs) |
| B6 | Volles Shell-Kontextmenü | Rechtsklick wie im Explorer (inkl. „Senden an", Kopieren etc.) |
| B7 | Verteil-Paket für Kollegen | Installer + kurze Anleitung; Kollege installiert, bekommt ab dann Updates automatisch. **Vorgabe (23.07.): Installation nach `C:\Program Files\ISDesk` (Standard-Programmordner, per-machine, Admin nur bei Installation/Update)** |

### C. SPÄTER — Phase 3 (nur bei Bedarf)

- Hotkey „alle Bereiche zeigen/verstecken", Quick-Hide per Doppelklick
- Suche über alle Bereiche
- Farb-Presets/Themes pro Bereich, eigene Titelleisten-Farben
- Export/Import von Layouts (Kollegen-Vorlagen: ein Standard-Layout für die Firma)
- Hilfe-Seite in der App, Mehrsprachigkeit (DE/EN)

### D. BEWUSST WEGGELASSEN (der Ballast, den du loswerden willst)

- ❌ Lizenzierung/Aktivierung/Konto-Zwang
- ❌ Telemetrie (MixPanel) & externer Crash-Reporter (BugSplat)
- ❌ Desktop Pages (mehrere Desktop-Seiten mit Wischen)
- ❌ Auto-Organisations-Regeln (Dateityp/Name/Datum-Sortierautomatik)
- ❌ Skin-Engine (spak/Lua), Stardock-Themes
- ❌ Explorer-Injection / Manipulation der echten Desktop-Icons (Hauptquelle der Fences-Instabilität)
- ❌ ARM64, 32-Bit — nur x64
- ❌ Peek, ausgeblendete Fences, Werbung für Object Desktop

## 2. Architektur (Empfehlung)

**Stack: C# / .NET 8 (LTS) / WPF, eine einzige EXE + Tray.** Passt zu eurer Firmen-Erfahrung
(WPF-Know-how vorhanden), erlaubt Acrylic/Blur + runde Ecken über die Windows-11-DWM-APIs und
bleibt ohne Web-Runtime schlank.

**Kernentscheidung — Icons (Empfehlung: Variante 1):**

| | Variante 1: Eigene Bereiche mit echten Ordnern ✅ | Variante 2: Desktop-Icons einfangen (wie Stardock) |
|---|---|---|
| Prinzip | Jede Fence = randloses Fenster, zeigt Ordnerinhalt | Overlay über Explorer-ListView, Icon-Positionen hijacken |
| Stabilität | Nur dokumentierte APIs, Windows-Update-fest | Undokumentiert, bricht regelmäßig, Explorer-Hooks |
| Aufwand | Überschaubar | Sehr hoch, Virenscanner-Risiko |
| Dein Desktop | Lose Icons bleiben unberührt normale Desktop-Icons; was du in Bereiche legst, wandert in deren Ordner | Alles bleibt „echt" auf dem Desktop |
| Nebenwirkung | Bereiche-Inhalte liegen als echte Ordner auf der Platte → über Explorer/Backup/Cloud erreichbar (eher ein Plus) | — |

Deine Fences enthalten heute fast nur Verknüpfungen — die Migration ist trivial (wir können
deine bestehende Fences-Aufteilung sogar automatisch aus dem Snapshot-XML übernehmen: gleiche
Bereiche, gleiche Icons, gleiche Positionen).

**Datenmodell (JSON in `%APPDATA%\<App>\config.json`):**

```
Fence: Id, Titel, Monitor, X/Y/Breite/Höhe (pro Monitor-Konfiguration),
       Transparenz, Blur an/aus, RollUp-Zustand, aktiver Tab
Tab:   Titel, Ordnerpfad, IconGröße, Sortierung
Global: Standard-Transparenz, Theme, Basisordner (D:\Fences), Autostart, UpdateCheck
```

**Fenster-Technik:** `WS_EX_TOOLWINDOW` + `WS_EX_NOACTIVATE` (kein Alt-Tab), per `SetWindowPos`
auf HWND_BOTTOM gepinnt, Reaktion auf „Desktop anzeigen"; Icons über Shell-API
(`IShellItemImageFactory`) inkl. Overlay-Pfeile; `FileSystemWatcher` je Tab-Ordner hält die
Ansicht live.

## 3. GitHub & Auto-Update

- Repo: `sabitzerm-isd/isdesk` (**privat**); Auto-Update aus dem privaten Repo läuft über ein
  schreibgeschütztes Token, das mit dem Installer ausgeliefert wird
- Jede Arbeitssitzung endet mit Commit + Push (Sicherung ab Tag 1)
- Release-Ablauf: Version hochzählen → Build → Installer (Inno Setup) → GitHub Release mit
  Installer als Asset → alle Installationen melden sich beim nächsten Start
- Dafür existiert bei uns ein bewährtes, fertiges Muster (UpdateService + Banner + Installer-
  Start), das ich 1:1 übernehme — das ist kein Experiment

## 4. Phasenplan (angepasst an dein Wochen-Limit)

| Phase | Inhalt | Umfang |
|---|---|---|
| **0 — heute** | Analyse ✅, Punkteplan ✅, deine Freigabe, Repo anlegen | klein |
| **1a** | Projektgerüst, Tray, Fence-Fenster (erstellen/verschieben/resizen), bottom-most, JSON-Config | 1 Session |
| **1b** | Ordner-Anbindung, Icon-Anzeige, Doppelklick-Start, Drag & Drop | 1 Session |
| **1c** | Tabs, Transparenz-Regler + Acrylic, Feinschliff Optik | 1 Session |
| **1d** | GitHub Release #1 + Auto-Update einbauen, Installer, Migration deiner bestehenden Fences aus dem Snapshot-XML | 1 Session |
| **2** | B1–B7 einzeln, in beliebiger Reihenfolge, je ~½–1 Session | nach Bedarf |
| **3** | C-Punkte nur auf Zuruf | — |

Jede Phase endet lauffähig + committed — du kannst jederzeit pausieren, ohne halbfertigen Stand.

**Token-Sparen:** Die Phasen 1a–1d sind nach dieser Planung bewusst so klar spezifiziert, dass
sie mit **Opus** statt Fable umgesetzt werden können (Modell wählst du in der Modell-Auswahl der
App pro Session). Rechenintensive Unteraufgaben delegiere ich zusätzlich an günstigere
Subagenten. Fable lohnt sich wieder bei kniffligen Architektur-/Debug-Fragen (z. B. Multi-Monitor).

## 4b. STAND 23.07.2026 (v0.15.2, installiert + verteilt)

**Phase 1 (A1–A10): vollständig erledigt.** Zusätzlich weit über den Plan hinaus:
farbige Symbol-Galerie, Live-Suche, Tab-Farben, manuelle Icon-Anordnung, Papierkorb
und andere Systemobjekte, Chrome-Lesezeichen-Import, Datei-Ablage mit Endungs-Regeln,
Platz-Gedächtnis, Sicherung mit Aufbewahrung, echtes Shell-Kontextmenü (dunkel).

**Phase 2:** B5 (Multi-Monitor-Layouts), B6 (Shell-Kontextmenü), B7 (Installer +
Auto-Update) erledigt. B3 teilweise (Icon-Größe global statt pro Tab; Sortierung
durch manuelle Anordnung gelöst).

### Offen

| Nr. | Punkt | Anmerkung |
|---|---|---|
| B1 | Ordner-Navigation im Bereich | Doppelklick auf Ordner öffnet aktuell den Explorer statt im Bereich hineinzunavigieren (Breadcrumb zurück) |
| B2 | Roll-up (nur Titelleiste) | Doppelklick auf den Titel ist mit „Umbenennen" belegt — bräuchte andere Geste |
| B3 | Icon-Größe **pro Tab** | Aktuell global für alle Bereiche |
| B4 | Automatische Layout-Snapshots | Manuelle Sicherung existiert; tägliche automatische fehlt |
| C | Raster beim Verschieben/Größe | Gebaut, auf Wunsch deaktiviert („später verbessern") |
| C | Hotkey Bereiche zeigen/verstecken | Aktuell nur Tray-Menü + Tray-Doppelklick |
| C | Export/Import von Layouts | Für ein Firmen-Standardlayout der Kollegen |
| C | Hilfe-Seite in der App | |
| C | Mehrsprachigkeit (DE/EN) | Aktuell nur Deutsch |
| — | Code-Signierung | Windows meldet „Unbekannter Herausgeber" (Zertifikat ~80–200 €/Jahr) |
| — | Weitere Browser für Lesezeichen | Aktuell nur Chrome (Edge/Firefox vorbereitet erweiterbar) |
| — | Tabs innerhalb eines Bereichs umsortieren | Verschieben in andere Bereiche geht bereits |
| — | Multi-Monitor-Test | Layouts je Bildschirm-Konfiguration gebaut, aber mit den 3 echten Setups (Mobil / Homeoffice / Dortmund) noch nicht erprobt |
| — | Ordner-Überwachung verschlanken | Optional: nur den sichtbaren Tab überwachen statt alle 38 |

## 5. Entscheidungen (23.07.2026, mit Michael abgestimmt)

1. **Icon-Ansatz:** Variante 1 — eigene Bereiche mit echten Ordnern ✅
2. **GitHub:** privates Repo `sabitzerm-isd/isdesk` ✅
3. **App-Name:** **ISDesk** ✅
4. **Basisordner:** `D:\Fences\<Bereichsname>` (pro Rechner umstellbar) ✅
