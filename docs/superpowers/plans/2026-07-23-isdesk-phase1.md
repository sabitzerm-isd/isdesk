# ISDesk Phase 1a–1c Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development oder superpowers:executing-plans. Steps nutzen `- [ ]`-Checkboxen.
> **Abweichung (bewusst, vom Nutzer gefordert — Token-Budget):** Voller Code nur für Interop-/Kernlogik; XAML/Boilerplate als präzise Spezifikation. Implementierer hat WPF-Kompetenz; Review durch Hauptsession.

**Goal:** Lauffähiges ISDesk-MVP: verschiebbare, größenveränderbare Desktop-Bereiche (bottom-most) mit Acrylic-Transparenz, Tabs (je Tab ein Ordner), Icon-Anzeige mit Start per Doppelklick, Drag & Drop, Tray-Icon, JSON-Persistenz, Erststart-Demo-Bereich.

**Architecture:** WPF-Tray-App ohne Hauptfenster. Jede Fence = randloses `FenceWindow` (WindowChrome, WS_EX_TOOLWINDOW, per WM_WINDOWPOSCHANGING dauerhaft auf HWND_BOTTOM). Optik via SetWindowCompositionAttribute (Acrylic/Tint) + DWM-Rundecken. Jeder Tab spiegelt einen echten Ordner (FileSystemWatcher). Config = JSON in %APPDATA%\ISDesk.

**Tech Stack:** .NET 8 (`net8.0-windows`), WPF + `UseWindowsForms` (NotifyIcon), System.Text.Json, xunit (nur Logik-Tests). Keine weiteren NuGet-Pakete für die App.

## Global Constraints

- Sprache UI: Deutsch. Code/Identifier: Englisch. Kommentare sparsam.
- Zielplattform: Windows 11 x64, .NET 8 (SDK 8.0.423 lokal vorhanden).
- Keine Adminrechte zur Laufzeit; Config unter `%APPDATA%\ISDesk\config.json`.
- Basisordner Standard: `D:\Fences` (konfigurierbar via `AppConfig.BaseFolder`).
- Fences liegen IMMER hinter normalen Fenstern, nie in Alt-Tab/Taskbar.
- Niemals bestehende Desktop-Dateien des Nutzers anfassen (Demo-Inhalte nur NEU erzeugen).
- Repo: https://github.com/sabitzerm-isd/isdesk, Branch main, nach jedem Task committen.
- Commit-Messages Deutsch, Format `feat: …` / `fix: …`, Footer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

## Dateistruktur (Endzustand)

```
ISDesk.sln
.gitignore                        (VS-Standard: bin/, obj/, .vs/, *.user)
src/ISDesk/ISDesk.csproj
src/ISDesk/App.xaml               (kein StartupUri; Ressourcen: Themes/Styles.xaml)
src/ISDesk/App.xaml.cs            (Mutex, ConfigService, FenceManager, Tray)
src/ISDesk/Models/AppConfig.cs    (AppConfig, FenceConfig, TabConfig)
src/ISDesk/Services/ConfigService.cs
src/ISDesk/Services/FenceManager.cs
src/ISDesk/Services/ShellIconProvider.cs
src/ISDesk/Services/FolderContents.cs
src/ISDesk/Services/ShortcutFactory.cs
src/ISDesk/Services/AutostartService.cs
src/ISDesk/Services/TrayService.cs
src/ISDesk/Interop/WindowBackdrop.cs
src/ISDesk/Interop/BottomMostBehavior.cs
src/ISDesk/Views/FenceWindow.xaml(.cs)
src/ISDesk/Views/InputDialog.xaml(.cs)
src/ISDesk/ViewModels/FenceViewModel.cs
src/ISDesk/ViewModels/TabViewModel.cs
src/ISDesk/ViewModels/IconItemViewModel.cs
src/ISDesk/Themes/Styles.xaml
src/ISDesk/Assets/ISDesk.ico      (liegt bereits im Repo — nicht neu erzeugen)
tests/ISDesk.Tests/ISDesk.Tests.csproj  (xunit)
tests/ISDesk.Tests/ConfigServiceTests.cs
tests/ISDesk.Tests/FolderContentsTests.cs
```

---

### Task 1: Scaffold

**Files:** Create `ISDesk.sln`, `src/ISDesk/ISDesk.csproj`, `App.xaml(.cs)`, `.gitignore`, leeres `Themes/Styles.xaml`.

**csproj (exakt):**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyVersion>0.1.0</AssemblyVersion>
    <FileVersion>0.1.0</FileVersion>
    <Version>0.1.0</Version>
    <ApplicationIcon>Assets\ISDesk.ico</ApplicationIcon>
    <SatelliteResourceLanguages>de</SatelliteResourceLanguages>
  </PropertyGroup>
  <ItemGroup>
    <Resource Include="Assets\ISDesk.ico" />
  </ItemGroup>
</Project>
```

`App.xaml`: `ShutdownMode="OnExplicitShutdown"`, MergedDictionary `Themes/Styles.xaml`. `App.xaml.cs`: vorerst leerer `OnStartup`-Override.

- [ ] Step 1: Dateien anlegen; `dotnet build ISDesk.sln` → Erwartung: Build succeeded, 0 Warnings relevant.
- [ ] Step 2: Commit `feat: Projektgeruest ISDesk (net8 WPF)`

### Task 2: Models + ConfigService (mit Tests)

**Files:** Create `Models/AppConfig.cs`, `Services/ConfigService.cs`, `tests/…` (beide Testdateien), Modify `ISDesk.sln` (Testprojekt aufnehmen).

**Interfaces (Produces):**
```csharp
public sealed class AppConfig { public string BaseFolder = @"D:\Fences"; public double DefaultOpacity = 0.75; public bool DefaultBlur = true; public List<FenceConfig> Fences = new(); }   // als { get; set; }-Properties!
public sealed class FenceConfig { Guid Id; string Title; double X,Y,Width,Height; double Opacity; bool Blur; int ActiveTab; List<TabConfig> Tabs; }
public sealed class TabConfig { string Title; string FolderPath; int IconSize = 32; }
public sealed class ConfigService {
  ConfigService(string? pathOverride = null);          // default %APPDATA%\ISDesk\config.json
  AppConfig Config { get; }
  void Load();                                          // fehlende/kaputte Datei → Defaults
  void Save();                                          // atomar: .tmp schreiben, File.Move(overwrite)
  void SaveDebounced();                                 // 400 ms, DispatcherTimer-frei (System.Timers), thread-safe
}
```
JSON via System.Text.Json, `WriteIndented=true`. Bei Deserialisierungsfehler: Backup `config.bad.json` anlegen, Defaults laden (kein Crash).

- [ ] Step 1: Tests schreiben (`ConfigServiceTests`): RoundTrip (Save→Load ergibt gleiche Werte inkl. 2 Fences/Tabs), CorruptFile (Müll-JSON → Defaults + .bad-Datei existiert). Temp-Pfad via `Path.GetTempFileName()`-Ordner.
- [ ] Step 2: `dotnet test` → rot. Implementieren. `dotnet test` → grün.
- [ ] Step 3: Commit `feat: Datenmodell + ConfigService (JSON, atomar, debounced)`

### Task 3: Interop — Acrylic + Bottom-Most + Fenstergerüst

**Files:** Create `Interop/WindowBackdrop.cs`, `Interop/BottomMostBehavior.cs`, `Views/FenceWindow.xaml(.cs)` (nur Rahmen: Titelzeile mit Titel-Text, leerer Inhalt), `ViewModels/FenceViewModel.cs` (Title, Opacity, Blur, Geometrie — INotifyPropertyChanged).

**WindowBackdrop.cs — VOLLSTÄNDIG SO ÜBERNEHMEN:**
```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ISDesk.Interop;

public static class WindowBackdrop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int AccentState; public int AccentFlags; public uint GradientColor; public int AnimationId; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_TRANSPARENTGRADIENT = 2;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    /// opacity 0..1 (Tint-Deckkraft), blur an/aus. tint = Grundfarbe (dunkel).
    public static void Apply(Window window, double opacity, bool blur, uint tintRgb = 0x1C1C1E)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        byte a = (byte)Math.Clamp((int)Math.Round(opacity * 255), 0, 255);
        // GradientColor-Format: 0xAABBGGRR
        uint abgr = ((uint)a << 24)
                  | ((tintRgb & 0x0000FF) << 16)      // R -> BB-Position
                  | (tintRgb & 0x00FF00)              // G bleibt
                  | ((tintRgb & 0xFF0000) >> 16);     // B -> RR-Position
        var accent = new AccentPolicy
        {
            AccentState = blur ? ACCENT_ENABLE_ACRYLICBLURBEHIND : ACCENT_ENABLE_TRANSPARENTGRADIENT,
            AccentFlags = 2,
            GradientColor = abgr
        };
        int size = Marshal.SizeOf(accent);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(ptr); }

        int on = 1; DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        int round = DWMWCP_ROUND; DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }
}
```

**BottomMostBehavior.cs — VOLLSTÄNDIG SO ÜBERNEHMEN:**
```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ISDesk.Interop;

public static class BottomMostBehavior
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private static readonly IntPtr HWND_BOTTOM = new(1);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    public static void Attach(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        HwndSource.FromHwnd(hwnd)!.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WINDOWPOSCHANGING)
        {
            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            wp.hwndInsertAfter = HWND_BOTTOM;      // nie über andere Fenster steigen
            Marshal.StructureToPtr(wp, lParam, false);
        }
        return IntPtr.Zero;
    }
}
```

**FenceWindow-Rahmen:** `WindowStyle=None`, `AllowsTransparency=False` (PFLICHT für Acrylic!), `Background=Transparent`, `ShowInTaskbar=False`, `ShowActivated=False`, `WindowChrome` (`CaptionHeight=0`, `ResizeBorderThickness=6`, `GlassFrameThickness=0`, `CornerRadius=0`). Titelzeile = Border Höhe 34, Background `#26000000`, TextBlock Titel (SemiBold, 12.5, `#DDFFFFFF`, Margin 12,0), `MouseLeftButtonDown → DragMove()`. In `OnSourceInitialized`: `BottomMostBehavior.Attach(this)` + `WindowBackdrop.Apply(this, vm.Opacity, vm.Blur)`. `LocationChanged`/`SizeChanged` → Geometrie in VM + `SaveDebounced`.
MinWidth 180, MinHeight 120.

- [ ] Step 1: Implementieren; Mini-Test in `App.OnStartup`: ein FenceWindow (400×260 bei 200,200) hart erzeugen und `Show()`.
- [ ] Step 2: `dotnet build` + Start `dotnet run --project src/ISDesk` → Sichtprüfung (Screenshot): dunkles, halbtransparent-verwischtes, rund-eckiges Fenster ohne Rahmen; bleibt hinter anderen Fenstern (z. B. Explorer davor ziehen); nicht in Alt-Tab. App wieder beenden (Prozess killen, Tray kommt später).
- [ ] Step 3: Commit `feat: FenceWindow-Geruest mit Acrylic und Bottom-Most`

### Task 4: Ordnerinhalt + Shell-Icons + Icon-Raster

**Files:** Create `Services/FolderContents.cs`, `Services/ShellIconProvider.cs`, `ViewModels/TabViewModel.cs`, `ViewModels/IconItemViewModel.cs`, `tests/FolderContentsTests.cs`; Modify `FenceWindow.xaml(.cs)`.

**Interfaces (Produces):**
```csharp
public static class FolderContents {
  // sichtbare Einträge: keine Hidden/System, kein desktop.ini/thumbs.db; Ordner zuerst, dann Dateien, je Name (OrdinalIgnoreCase)
  public static IReadOnlyList<string> ListVisibleEntries(string folderPath);
  // Anzeigename: .lnk/.url/.appref-ms → Dateiname ohne Extension, sonst Dateiname mit Extension; Ordner → Ordnername
  public static string GetDisplayName(string path);
}
public sealed class ShellIconProvider {                 // Singleton via static Instance
  public Task<ImageSource?> GetIconAsync(string path, int size);   // Cache (path+size, case-insensitive), Hintergrund-Thread, ImageSource ge-Freezed
}
public sealed class IconItemViewModel { string Path; string DisplayName; ImageSource? Icon; bool IsFolder; }
public sealed class TabViewModel {                      // pro Tab
  TabViewModel(TabConfig cfg, Action persist);
  string Title; string FolderPath; int IconSize;
  ObservableCollection<IconItemViewModel> Items;
  void StartWatching();                                  // FileSystemWatcher (Created/Deleted/Renamed/Changed), 300 ms Debounce → Reload auf UI-Thread
  void Reload(); void Dispose();
}
```

**ShellIconProvider-Kern (IShellItemImageFactory, VOLLSTÄNDIG SO ÜBERNEHMEN):**
```csharp
[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
private interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }
[StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
[DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
[DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
private const int SIIGBF_RESIZETOFIT = 0x00, SIIGBF_ICONONLY = 0x04;
// GetImage mit ICONONLY; HBITMAP → Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()) → Freeze() → DeleteObject(hbm).
// Fehler (Pfad weg etc.) → null. Aufruf in Task.Run; STA nicht nötig.
```

**Icon-Raster im FenceWindow:** pro Tab ein `ListBox` (ScrollViewer.VerticalScrollBarVisibility=Auto, ItemsPanel=WrapPanel, `ScrollViewer.HorizontalScrollBarVisibility=Disabled`, Hintergrund transparent, BorderThickness 0, `VirtualizingPanel` aus — Mengen sind klein). ItemTemplate: StackPanel Breite 84 (Bild 32×32 zentriert bei IconSize 32; Höhe = IconSize+40), TextBlock 11px, `TextTrimming=CharacterEllipsis`, `TextWrapping=Wrap`, `MaxHeight=30`, zentriert, Foreground `#F2FFFFFF`, leichte DropShadow (BlurRadius 3, Opacity .6). ItemContainerStyle: CornerRadius 6 Hover `#22FFFFFF`, Selected `#33FFFFFF`, kein Fokus-Rechteck.
Doppelklick auf Item: `Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })`; bei Ordner genauso (öffnet Explorer). Fehler → nicht crashen (try/catch, Debug.WriteLine).

- [ ] Step 1: `FolderContentsTests` (Temp-Ordner: versteckte Datei wird gefiltert, Sortierung Ordner-zuerst, DisplayName-Regeln .lnk/.txt) → rot → implementieren → grün (`dotnet test`).
- [ ] Step 2: FenceWindow bindet Demo-Tab auf `C:\Users\Public\Desktop` (nur LESEN) → Start → Icons + Namen sichtbar, Doppelklick startet Notepad++-Verknüpfung. Screenshot-Sichtprüfung.
- [ ] Step 3: Commit `feat: Ordnerinhalt, Shell-Icons, Icon-Raster mit Doppelklick-Start`

### Task 5: Tabs

**Files:** Modify `FenceWindow.xaml(.cs)`, `FenceViewModel.cs` (`ObservableCollection<TabViewModel> Tabs`, `TabViewModel? ActiveTab`), Create Teile in `Views/InputDialog.xaml(.cs)`.

Tab-Leiste unter der Titelzeile (Höhe 28): ItemsControl horizontal; Tab = Border CornerRadius 6, Padding 10,3, Text 11.5px `#CCFFFFFF`; aktiv: Background `#2EFFFFFF`, Text weiß. Klick wechselt `ActiveTab` (+ `ActiveTab`-Index persistieren). Ganz rechts „+"-Button (28×22, nur bei Maus-über-Fenster sichtbar via Trigger auf `IsMouseOver` des Fensters): legt Tab an → `InputDialog` („Name des Tabs"), Ordner = `<BaseFolder>\<FenceTitle>\<TabName>` — bei Kollision ` (2)` anhängen; `Directory.CreateDirectory`. Tab-Kontextmenü: „Umbenennen…" (nur Titel, Ordner bleibt), „Tab entfernen" (nur aus Config; Ordner bleibt — MessageBox-Hinweis), deaktiviert wenn letzter Tab.
`InputDialog`: kleines dunkles Fenster (Topmost, zentriert auf Owner-Fence, WindowStyle=None, Acrylic via `WindowBackdrop.Apply(…, 0.95, true)`), TextBox + OK/Abbrechen, Enter/Esc. **Achtung:** Owner-Fence ist bottom-most — Dialog deshalb OHNE Owner-Beziehung erzeugen und `Topmost=true`.

- [ ] Step 1: Implementieren; Start: 2 Tabs anlegen, wechseln, umbenennen, entfernen; App neustarten → Zustand erhalten (Persistenz über FenceManager kommt in Task 7 — hier reicht: Tab-Änderungen rufen `persist`-Callback).
- [ ] Step 2: Commit `feat: Tabs pro Bereich (anlegen, wechseln, umbenennen, entfernen)`

### Task 6: Drag & Drop + Icon-Kontextmenü

**Files:** Modify `FenceWindow.xaml.cs`, `TabViewModel.cs`, `IconItemViewModel`-Nutzung.

**Rein:** `AllowDrop=True` auf Fenster; `DragOver`: FileDrop → Effekt Move (Copy bei gedrückter Strg); `Drop`: für jede Quelle `File.Move`/`Directory.Move` in aktiven Tab-Ordner (gleiches Volume) sonst Copy+Delete-Fallback; Namenskollision → ` (2)`. Quelle == Zielordner → ignorieren. Fehler einzeln abfangen (MessageBox gesammelt am Ende: „3 Elemente konnten nicht verschoben werden: …").
**Raus:** `MouseMove` mit gedrückter linker Taste ab 4px Distanz → `DragDrop.DoDragDrop(listBox, new DataObject(DataFormats.FileDrop, new[]{path}), DragDropEffects.Move|Copy)`; nach Move-Effekt kein manuelles Löschen (Ziel-Explorer übernimmt Move selbst — Watcher aktualisiert die Ansicht).
**Icon-Kontextmenü:** Öffnen · Im Explorer anzeigen (`explorer.exe /select,"<path>"`) · Umbenennen… (InputDialog, `File.Move`/`Directory.Move` im selben Ordner) · In den Papierkorb (`Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile/DeleteDirectory` mit `RecycleOption.SendToRecycleBin`, `UIOption.OnlyErrorDialogs`; Rückfrage-MessageBox davor).

- [ ] Step 1: Implementieren; manuelle Prüfung: Datei aus Explorer in Fence ziehen (verschwindet dort, erscheint hier), rausziehen auf Desktop, umbenennen, Papierkorb-Löschen inkl. Wiederherstellbarkeit.
- [ ] Step 2: Commit `feat: Drag&Drop rein/raus + Icon-Kontextmenue`

### Task 7: Fence-Verwaltung + Persistenz komplett

**Files:** Create `Services/FenceManager.cs`; Modify `App.xaml.cs`, `FenceWindow.xaml(.cs)`, `FenceViewModel.cs`.

**Interfaces (Produces):**
```csharp
public sealed class FenceManager {
  FenceManager(ConfigService config);
  void OpenAll();                                   // je FenceConfig ein FenceWindow
  FenceWindow CreateFence(string title, Point? at); // legt Ordner <BaseFolder>\<title> + Standard-Tab "Allgemein" an, öffnet Fenster, persistiert
  void RemoveFence(FenceViewModel vm);              // Fenster zu, aus Config raus (Ordner bleibt!), persistiert
  void ShutdownAll();
}
```
**Fence-Kontextmenü (auf Fenster-Hintergrund):** Neuer Tab · Bereich umbenennen… · — · Transparenz (MenuItem mit eingebettetem Slider 10–100 %, live: `WindowBackdrop.Apply` bei ValueChanged + persist) · Hintergrund-Blur (IsCheckable) · — · Ordner im Explorer öffnen · Neuer Bereich · Bereich entfernen… (Bestätigung; Hinweis „Ordner bleibt erhalten") · — · ISDesk beenden.
Geometrie-Persistenz: bereits verkabelte `LocationChanged/SizeChanged` → `FenceConfig` aktualisieren + `SaveDebounced()`. Beim Laden: Fenster, deren gespeicherte Position außerhalb aller Screens liegt (`System.Windows.Forms.Screen.AllScreens`), auf Primärmonitor 100,100 zurückholen.

- [ ] Step 1: Implementieren; Prüfung: 2 Bereiche anlegen, verschieben/resizen, Transparenz je Bereich unterschiedlich, App beenden (noch via Taskkill) und neu starten → alles exakt wie zuvor.
- [ ] Step 2: Commit `feat: FenceManager, Kontextmenue, vollstaendige Persistenz`

### Task 8: Tray, Single-Instance, Autostart, Erststart-Demo

**Files:** Create `Services/TrayService.cs`, `Services/AutostartService.cs`, `Services/ShortcutFactory.cs`; Modify `App.xaml.cs`.

- Single-Instance: benannter `Mutex("Global\\ISDesk_SingleInstance")` in `OnStartup`; zweite Instanz beendet sich still.
- `TrayService`: `System.Windows.Forms.NotifyIcon`, Icon aus `Assets/ISDesk.ico` (`GetResourceStream`), Text „ISDesk". Menü: Neuer Bereich · Alle Bereiche neu ausrichten (alle Fenster wieder in sichtbaren Bereich holen) · Autostart (Checked-Toggle) · — · Beenden (`FenceManager.ShutdownAll` + `Shutdown()`). Doppelklick aufs Tray = Neuer Bereich.
- `AutostartService`: HKCU `Software\Microsoft\Windows\CurrentVersion\Run`, Wert `ISDesk` = `"<exe-Pfad>"`; IsEnabled/Enable/Disable.
- `ShortcutFactory.CreateLnk(string lnkPath, string target)`: COM dynamic `WScript.Shell` → `CreateShortcut`, `TargetPath`, `Save()`.
- Erststart (Config hatte keine Fences): Bereich „Willkommen" (420×300 bei 15 % Bildschirmbreite rechts oben) mit Tab „Allgemein" → Ordner `D:\Fences\Willkommen\Allgemein`; darin Verknüpfungen: Editor (`C:\Windows\System32\notepad.exe`), Paint (`mspaint.exe`), Explorer (`explorer.exe`) sowie `Fences-Ordner.lnk` → `D:\Fences`. NIEMALS bestehende Nutzer-Dateien anfassen.
- App beendet sich sauber: Watcher disposed, Tray disposed, finaler `Save()`.

- [ ] Step 1: Implementieren; Prüfung: Erststart-Demo erscheint, Tray-Menü vollständig, zweite Instanz startet nicht, Autostart-Toggle schreibt/entfernt Registry-Wert (`reg query HKCU\...\Run /v ISDesk`).
- [ ] Step 2: Commit `feat: Tray, Single-Instance, Autostart, Erststart-Demo`

### Task 9: Smoke-Test + README

- [ ] Step 1: Kompletter Durchlauf: `dotnet build -c Release`; Start aus Release; PowerShell-Screenshot; Sichtprüfung Optik (Acrylic, runde Ecken, dunkle Titelzeile, Tabs); `dotnet test` grün.
- [ ] Step 2: `README.md`: Was ist ISDesk, Features Phase 1, Build (`dotnet build`), Start, Config-Pfad, bekannte Einschränkungen („Win+D blendet Bereiche mit aus — geplant Phase 2", „Update-Mechanik folgt in Phase 1d").
- [ ] Step 3: Commit `docs: README` + `git push`.

## Nicht in diesem Plan (bewusst)

Auto-Update/Installer/Release (Phase 1d, eigener Plan mit github-app-update-mechanism-Skill), Migration des Fences-Snapshots (1d), Roll-up, Ordner-Navigation im Tab, Multi-Monitor-Layoutlogik, Einstellungen-Fenster (Phase 2).
