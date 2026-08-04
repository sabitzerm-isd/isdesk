using System.IO;
using MSDesk.Models;
using MSDesk.Services;

namespace MSDesk.Tests;

/// <summary>
/// Der Ziel-Schluessel entscheidet, was MSDesk fuer DIESELBE Verknuepfung
/// haelt — und damit, was beim Einsortieren entfernt oder ersetzt wird.
///
/// Er bestand frueher nur aus dem Dateinamen des Zielprogramms. Damit galt
/// jede Verknuepfung auf dasselbe Programm als dieselbe Sache: zwei
/// Arbeitsmappen ueber excel.exe, zwei Server ueber mstsc.exe, zwei
/// Anwendungen ueber chrome.exe. Eine davon wurde dann als „doppelt" entfernt.
/// Genau so ist ein „Planungsmanager" verschwunden.
///
/// Diese Tests halten fest, dass die ARGUMENTE mit in den Schluessel gehoeren.
/// </summary>
public class ZielSchluesselTests : IDisposable
{
    private readonly string _root;

    public ZielSchluesselTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msdesk_ziel_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        PlacementRegistry.ClearTargetCache();
    }

    public void Dispose()
    {
        PlacementRegistry.ClearTargetCache();
        try { Directory.Delete(_root, true); } catch (IOException) { /* egal */ }
    }

    /// Legt eine echte .lnk an. Ohne Ziel-Datei waere nichts aufloesbar.
    private string Verknuepfung(string name, string ziel, string argumente = "")
    {
        var pfad = Path.Combine(_root, name);
        ShortcutFactory.CreateLnk(pfad, ziel);

        if (argumente.Length > 0) ArgumenteSetzen(pfad, argumente);
        return pfad;
    }

    private static void ArgumenteSetzen(string lnkPfad, string argumente)
    {
        var typ = Type.GetTypeFromProgID("WScript.Shell");
        Assert.NotNull(typ);
        dynamic shell = Activator.CreateInstance(typ!)!;
        dynamic sc = shell.CreateShortcut(lnkPfad);
        sc.Arguments = argumente;
        sc.Save();
    }

    [Fact]
    public void GleichesProgramm_UnterschiedlicheArgumente_SindNICHTGleich()
    {
        // Der gemeldete Verlust in einem Satz.
        var planung = Verknuepfung("Planungsmanager.lnk", @"C:\Windows\System32\notepad.exe",
                                   @"C:\Plan\Planung.txt");
        var angebote = Verknuepfung("Angebote.lnk", @"C:\Windows\System32\notepad.exe",
                                    @"C:\Plan\Angebote.txt");

        var a = PlacementRegistry.ZielSchluessel(planung);
        var b = PlacementRegistry.ZielSchluessel(angebote);

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void GleichesProgramm_GleicheArgumente_SindGleich()
    {
        // Der Fall, den das Aufraeumen treffen SOLL: dieselbe Verknuepfung zweimal.
        var eins = Verknuepfung("Editor.lnk", @"C:\Windows\System32\notepad.exe");
        var zwei = Verknuepfung("Editor - Kopie.lnk", @"C:\Windows\System32\notepad.exe");

        Assert.Equal(PlacementRegistry.ZielSchluessel(eins),
                     PlacementRegistry.ZielSchluessel(zwei));
    }

    [Fact]
    public void OhneArgumente_SchluesselIstNurDasProgramm()
    {
        // Verknuepfungen ohne Argumente sollen weiter ueber Programm-Updates
        // hinweg wiedererkannt werden (neuer Versionsordner, gleiche .exe).
        var lnk = Verknuepfung("Editor.lnk", @"C:\Windows\System32\notepad.exe");

        Assert.Equal("notepad.exe", PlacementRegistry.ZielSchluessel(lnk));
    }

    [Fact]
    public void KeineVerknuepfung_LiefertNull()
    {
        var datei = Path.Combine(_root, "Notiz.txt");
        File.WriteAllText(datei, "x");

        Assert.Null(PlacementRegistry.ZielSchluessel(datei));
    }
}
