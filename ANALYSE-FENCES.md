# Analyse: Stardock Fences 4.2.3.6 auf diesem Rechner

Stand: 23.07.2026 — Basis: installierte Version `C:\Program Files (x86)\Stardock\Fences`,
Konfiguration/Snapshots unter `%APPDATA%\Stardock\Fences` und Desktop-Screenshot vom 23.07.2026 08:22.

## 1. Deine tatsächliche Nutzung (aus DailySnapshot0.xml)

**8 Fences, alle auf dem Hauptmonitor (3440×1440, DISPLAY5), Layout „Right side"** —
alles rechts oben gebündelt, Bildschirmmitte bleibt frei:

| Fence | Inhalt (Beispiele) | Besonderheit |
|---|---|---|
| Programs | Bambu Studio, CapCut, Notepad++, GIMP, Rhino 8, Firefox … | breiter Balken ganz oben |
| Cloud | Dropbox, Google Drive, HiDrive | klein, 3 Icons |
| Kunden | Wiplinger | klein |
| Programme | FreeFileSync, Total Commander, eluCad, TeamViewer, Camtasia | |
| Verknüpfungen | Ordner-Links: Kunden, PM, Inkremente, work4all, Reisekosten … | fast nur Ordner-Verknüpfungen |
| Remote | RDP-Dateien (QS01, QS110, ISD-Remote), Sophos Connect, ISDLauncher | |
| Eigene Tools | PlanungsAPP, Admin Tool, Skill-Galerie, Voxly, TREPEDIA, Antigravity | deine eigenen Apps |
| ISD | ISD Wiki, OWA, Zeus, Open STEP Viewer … | **zusammengerollt** (nur Titelleiste sichtbar) |

Dazu: ~40 lose Icons/Dateien **außerhalb** der Fences (SZA-Arbeitsdateien, Bilder, TXT) —
v. a. am linken Bildschirmrand als „Ablagefläche".

**Aktive Einstellungen:**
- Dunkle, halbtransparente Fences (einheitlicher Stil, dezent)
- Ordner-Navigation **innerhalb** von Fences wird aktiv genutzt (ViewStates für `D:\400 PM`, `D:\950 Inkremente\3002\…`, `D:\800 Cloud\HiDrive`, `D:\500 Kunden` — inkl. Icon-Größe/Sortierung pro Ordner)
- Tägliche Layout-Snapshots (XML + Screenshot) laufen
- Roll-up wird genutzt (ISD-Fence), RollupOnDock=an
- Sprache de-DE, eigenes Icon-Raster (Spacing 108×69), „Neue Icons ans Ende"
- **Nicht genutzt:** Quick-Hide (aus), automatische Desktop-Sortierung (aus), Desktop Pages, Regeln

**Umgebung:** 2 Monitore (3440×1440 primär + 2560×1600), häufige Auflösungswechsel
(Dutzende MultiMonitor-Logs → Docking/RDP). Fences kämpft damit sichtbar (eigene Debug-Logs dafür).

## 2. Feature-Inventar Fences 4 (was das Original alles mitbringt)

| Feature | Von dir genutzt? |
|---|---|
| Bereiche (Fences) mit Titel, Farbe, Transparenz | ✅ Kern |
| Icons per Drag & Drop in Bereiche | ✅ Kern |
| Ordner-Portale + Navigation in der Fence | ✅ ja |
| Layout-Snapshots + Wiederherstellen | ✅ (automatisch) |
| Roll-up (nur Titelleiste) | ✅ (ISD-Fence) |
| Multi-Monitor + Auflösungswechsel-Handling | ✅ (zwangsläufig) |
| Icon-Größe/Abstand/Sortierung pro Fence | ✅ teils |
| Quick-Hide (Doppelklick → Desktop leer) | ❌ deaktiviert |
| Desktop Pages (mehrere Desktop-Seiten) | ❌ |
| Auto-Organisation nach Regeln (Typ/Name/Datum) | ❌ deaktiviert |
| Ausgeblendete Fences / Peek | ❌ |
| Skins/Themes (spak, Lua-Engine) | ❌ Standard-Optik |
| Shell-Kontextmenü-Integration (FencesMenu.dll) | ❌ kaum relevant |
| Lizenzierung (CryptoLicensing), Stardock-Konto | — Ballast |
| Telemetrie (MixPanel), Crash-Reporter (BugSplat) | — Ballast |
| Eigener Updater (SasUpgrade) | — ersetzen wir durch GitHub |
| Mehrsprachigkeit (Lang-Ordner) | de reicht |
| ARM64-Support | ❌ nicht nötig |

## 3. Technische Erkenntnisse aus dem Original

- Fences manipuliert die **echten Desktop-Icons** (Explorer-ListView) — das ist der Grund für
  Explorer-Hooks (FencesMenu*.dll in 32/64/ARM-Varianten), Fragilität bei Windows-Updates und
  die vielen Debug-/Crash-Werkzeuge. **Für den Nachbau nicht empfehlenswert.**
- Der stabile, saubere Weg für einen Eigenbau: Jede Fence ist ein eigenes randloses Fenster,
  das den Inhalt eines echten Ordners anzeigt (Verknüpfungen/Dateien). Der Windows-Desktop
  bleibt unangetastet — lose Icons funktionieren weiter wie bisher.
- Deine Nutzung passt exakt zu diesem Modell: Deine Fences enthalten fast ausschließlich
  Verknüpfungen (.lnk/.url/.rdp), und Ordner-Ansichten in der Fence nutzt du sowieso schon.
