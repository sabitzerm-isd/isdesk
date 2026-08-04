using System.IO;
using MSDesk.Models;
using MSDesk.Services;

namespace MSDesk.Tests;

/// <summary>
/// Symbole nach einem Programm-Update wieder einordnen.
///
/// Der Vorgang muss GEZIELT arbeiten: nur Eintraege anfassen, deren Platz
/// bekannt ist. Alles andere bleibt auf dem Desktop liegen — sonst wuerde ein
/// Knopfdruck den ganzen Desktop leerraeumen.
/// </summary>
public class DesktopReclaimTests : IDisposable
{
    private readonly string _root;
    private readonly string _bereich;
    private readonly ConfigService _config;

    public DesktopReclaimTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msdesk_reclaim_" + Guid.NewGuid().ToString("N")[..8]);
        _bereich = Path.Combine(_root, "Programme");
        Directory.CreateDirectory(_bereich);

        _config = new ConfigService(Path.Combine(_root, "config.json"));
        _config.Config.Fences.Add(new FenceConfig
        {
            Title = "Programme",
            Tabs = { new TabConfig { Title = "Allgemein", FolderPath = _bereich } }
        });
        PlacementRegistry.Init(_config);
        PlacementRegistry.ClearTargetCache();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { /* egal */ }
    }

    private string Datei(string ordner, string name, string inhalt = "x")
    {
        Directory.CreateDirectory(ordner);
        var pfad = Path.Combine(ordner, name);
        File.WriteAllText(pfad, inhalt);
        return pfad;
    }

    [Fact]
    public void Lookup_FindetGelerntenPlatzUeberDenNamen()
    {
        var datei = Datei(_bereich, "Camtasia.txt");
        PlacementRegistry.Learn(datei, _bereich);

        Assert.Equal(_bereich, PlacementRegistry.Lookup("camtasia.txt"));
        Assert.Equal(_bereich, PlacementRegistry.Lookup("Camtasia.txt")); // Gross-/Kleinschreibung egal
    }

    [Fact]
    public void Lookup_UnbekannterName_LiefertNull()
    {
        Assert.Null(PlacementRegistry.Lookup("gibtesnicht.txt"));
    }

    [Fact]
    public void Lookup_OrdnerWeg_LiefertNull()
    {
        var weg = Path.Combine(_root, "Geloescht");
        Directory.CreateDirectory(weg);
        var datei = Datei(weg, "Test.txt");
        PlacementRegistry.Learn(datei, weg);
        Directory.Delete(weg, true);

        // Ein Bereich, den es nicht mehr gibt, darf kein Ziel sein.
        Assert.Null(PlacementRegistry.Lookup("test.txt"));
    }

    [Fact]
    public void ZielSchluessel_NurFuerVerknuepfungen()
    {
        var normal = Datei(_bereich, "Notiz.txt");

        // Keine .lnk → gar kein Aufloesen, also auch kein Ziel.
        Assert.Null(PlacementRegistry.ZielSchluessel(normal));
    }

    [Fact]
    public void ZielSchluessel_KaputteVerknuepfung_LiefertNullOhneFehler()
    {
        // Eine Datei mit der Endung .lnk, die keine echte Verknuepfung ist.
        var kaputt = Datei(_bereich, "Kaputt.lnk", "kein gueltiger Inhalt");

        Assert.Null(PlacementRegistry.ZielSchluessel(kaputt));
    }

    [Fact]
    public void Vorschau_VeraendertNichts()
    {
        var datei = Datei(_bereich, "Werkzeug.txt");
        PlacementRegistry.Learn(datei, _bereich);

        var vorher = Directory.GetFiles(_bereich).Length;
        DesktopReclaim.Run(_config, nurVorschau: true);

        Assert.Equal(vorher, Directory.GetFiles(_bereich).Length);
    }

    [Fact]
    public void Duplikate_Vorschau_LoeschtNichts()
    {
        Datei(_bereich, "A.lnk");
        Datei(_bereich, "B.lnk");

        var entfernt = DesktopReclaim.RemoveDuplicates(_config, nurVorschau: true);

        // Beide Dateien sind keine echten Verknuepfungen → kein Ziel → kein Duplikat.
        Assert.Equal(0, entfernt);
        Assert.Equal(2, Directory.GetFiles(_bereich, "*.lnk").Length);
    }

    [Fact]
    public void Duplikate_OhneBereiche_KeinFehler()
    {
        var leer = new ConfigService(Path.Combine(_root, "leer.json"));
        Assert.Equal(0, DesktopReclaim.RemoveDuplicates(leer, nurVorschau: true));
    }

    [Fact]
    public void Ergebnis_ZaehltGesamtRichtig()
    {
        var e = new DesktopReclaim.Ergebnis(Zurueckgeholt: 3, Ersetzt: 2, Fehlgeschlagen: 1,
                                            Gesperrt: new[] { "Camtasia.lnk" });
        // Weder Fehlgeschlagene noch Gesperrte zaehlen als erledigt.
        Assert.Equal(5, e.Gesamt);
        Assert.Single(e.Gesperrt);
    }

    [Fact]
    public void Duplikate_UnlesbareVerknuepfungen_WerdenNICHTAngefasst()
    {
        // Frueher wurden zwei gleichnamige .lnk allein wegen des Namens als
        // Doppelte behandelt, sobald ihr Ziel nicht ermittelbar war. „Nicht
        // ermittelbar" heisst aber nicht „kaputt": ein Netzlaufwerk kann
        // getrennt, eine Wechselplatte abgezogen, COM kurz belegt sein. Auf
        // diesem Weg sind intakte Verknuepfungen verschwunden.
        var zweiterTab = Path.Combine(_root, "Administration");
        Directory.CreateDirectory(zweiterTab);
        _config.Config.Fences[0].Tabs.Add(new TabConfig { Title = "Administration", FolderPath = zweiterTab });

        Datei(_bereich, "Admin Tool.lnk", "nicht lesbar A");
        Datei(zweiterTab, "Admin Tool.lnk", "nicht lesbar B");

        var gefunden = DesktopReclaim.RemoveDuplicates(_config, nurVorschau: true);

        Assert.Equal(0, gefunden);
        Assert.Equal(2, Directory.GetFiles(_root, "Admin Tool.lnk", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void Duplikate_NachweislichInsLeere_WerdenErkannt()
    {
        // Der Fall, den das Aufraeumen treffen SOLL: zwei Mal derselbe Name,
        // beide zeigen auf eine Datei, die es auf einem VORHANDENEN Laufwerk
        // nachweislich nicht gibt.
        var zweiterTab = Path.Combine(_root, "Administration");
        Directory.CreateDirectory(zweiterTab);
        _config.Config.Fences[0].Tabs.Add(new TabConfig { Title = "Administration", FolderPath = zweiterTab });

        var totesZiel = Path.Combine(_root, "gibtesnicht", "AdminTool.exe");
        ShortcutFactory.CreateLnk(Path.Combine(_bereich, "Admin Tool.lnk"), totesZiel);
        ShortcutFactory.CreateLnk(Path.Combine(zweiterTab, "Admin Tool.lnk"), totesZiel);
        PlacementRegistry.ClearTargetCache();

        var gefunden = DesktopReclaim.RemoveDuplicates(_config, nurVorschau: true);

        Assert.Equal(1, gefunden); // eine von zweien fliegt
    }

    [Fact]
    public void Duplikate_GleichesProgrammVerschiedeneArgumente_BleibenBeide()
    {
        // Der gemeldete Verlust: „Planungsmanager" und eine andere Verknuepfung
        // starten dasselbe Programm mit verschiedenen Dateien. Das sind zwei
        // Dinge, keine Doppelten.
        var zweiterTab = Path.Combine(_root, "Planung");
        Directory.CreateDirectory(zweiterTab);
        _config.Config.Fences[0].Tabs.Add(new TabConfig { Title = "Planung", FolderPath = zweiterTab });

        MitArgumenten(Path.Combine(_bereich, "Angebote.lnk"),
                      @"C:\Windows\System32\notepad.exe", @"C:\Plan\Angebote.txt");
        MitArgumenten(Path.Combine(zweiterTab, "Planungsmanager.lnk"),
                      @"C:\Windows\System32\notepad.exe", @"C:\Plan\Planung.txt");
        PlacementRegistry.ClearTargetCache();

        Assert.Equal(0, DesktopReclaim.RemoveDuplicates(_config, nurVorschau: true));
    }

    private static void MitArgumenten(string lnkPfad, string ziel, string argumente)
    {
        ShortcutFactory.CreateLnk(lnkPfad, ziel);

        var typ = Type.GetTypeFromProgID("WScript.Shell");
        Assert.NotNull(typ);
        dynamic shell = Activator.CreateInstance(typ!)!;
        dynamic sc = shell.CreateShortcut(lnkPfad);
        sc.Arguments = argumente;
        sc.Save();
    }
}
