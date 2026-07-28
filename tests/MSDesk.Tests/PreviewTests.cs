using System.IO;
using MSDesk.Models;
using MSDesk.ViewModels;

namespace MSDesk.Tests;

/// <summary>
/// Vorschau beim Verweilen auf der Bereichs-Ueberschrift bzw. auf einem nicht
/// aktiven Reiter.
///
/// Wichtigste Zusicherung: Die Vorschau darf das verzoegerte Laden der Tabs
/// nicht aushebeln. Wuerde sie beim Hinsehen den Tab laden, waere die dadurch
/// gesparte Speicherlast dahin — und genau die war ein erklaertes Ziel.
/// </summary>
public class PreviewTests : IDisposable
{
    private readonly string _root;

    public PreviewTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msdesk_preview_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { /* egal */ }
    }

    private string TabOrdner(string name, params string[] dateien)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        foreach (var datei in dateien) File.WriteAllText(Path.Combine(folder, datei), "x");
        return folder;
    }

    private FenceViewModel Bereich(params TabConfig[] tabs)
    {
        var config = new FenceConfig { Title = "Test" };
        config.Tabs.AddRange(tabs);
        return new FenceViewModel(config, _root);
    }

    [Fact]
    public void Reiter_NichtGeladen_LiefertNamenUndBleibtUngeladen()
    {
        var ordner = TabOrdner("Programme", "Word.lnk", "Excel.lnk");
        var vm = Bereich(
            new TabConfig { Title = "Allgemein", FolderPath = TabOrdner("Allgemein", "a.txt") },
            new TabConfig { Title = "Programme", FolderPath = ordner });

        var zweiter = vm.Tabs[1];
        Assert.False(zweiter.IsLoaded);

        zweiter.RefreshPreview();

        Assert.Equal(new[] { "Excel", "Word" }, zweiter.PreviewNames.OrderBy(n => n).ToArray());

        // Der Kern: die Vorschau hat den Tab NICHT geladen.
        Assert.False(zweiter.IsLoaded);
        Assert.Empty(zweiter.Items);
    }

    [Fact]
    public void Reiter_MehrAlsZwoelf_WirdGekappt()
    {
        var dateien = Enumerable.Range(1, 20).Select(i => $"Datei{i:00}.txt").ToArray();
        var vm = Bereich(new TabConfig { Title = "Viele", FolderPath = TabOrdner("Viele", dateien) });

        var tab = vm.Tabs[0];
        tab.RefreshPreview();

        Assert.Equal(12, tab.PreviewNames.Count);
        Assert.Equal(8, tab.PreviewMore);
        Assert.Equal("… und 8 weitere", tab.PreviewMoreText);
    }

    [Fact]
    public void Reiter_GenauZwoelf_KeinHinweisAufWeitere()
    {
        var dateien = Enumerable.Range(1, 12).Select(i => $"Datei{i:00}.txt").ToArray();
        var vm = Bereich(new TabConfig { Title = "Zwoelf", FolderPath = TabOrdner("Zwoelf", dateien) });

        var tab = vm.Tabs[0];
        tab.RefreshPreview();

        Assert.Equal(12, tab.PreviewNames.Count);
        Assert.Equal(0, tab.PreviewMore);
        Assert.Equal("", tab.PreviewMoreText);
    }

    [Fact]
    public void Reiter_FehlenderOrdner_IstLeerOhneFehler()
    {
        var vm = Bereich(new TabConfig
        {
            Title = "Weg",
            FolderPath = Path.Combine(_root, "gibtesnicht")
        });

        var tab = vm.Tabs[0];
        tab.RefreshPreview(); // darf nicht werfen

        Assert.True(tab.PreviewEmpty);
        Assert.Empty(tab.PreviewNames);
    }

    [Fact]
    public void Bereich_MehrereReiter_ZeigtJedenReiter()
    {
        var vm = Bereich(
            new TabConfig { Title = "Allgemein", FolderPath = TabOrdner("A", "a.txt", "b.txt") },
            new TabConfig { Title = "Trac", FolderPath = TabOrdner("T", "t.txt") });

        vm.RefreshPreview();

        // Ohne bekannte Anzahl steht der blanke Titel da — nachgelesen wird
        // dafuer nichts.
        Assert.Equal(new[] { "Allgemein", "Trac" }, vm.PreviewLines.ToArray());
        Assert.Equal("2 Reiter", vm.PreviewHint);

        // Ist die Anzahl dagegen ohnehin bekannt, wird sie auch gezeigt.
        vm.Tabs[0].RefreshPreview(); // waermt den Namens-Zwischenspeicher
        vm.RefreshPreview();

        Assert.Equal(new[] { "Allgemein (2)", "Trac" }, vm.PreviewLines.ToArray());
    }

    [Fact]
    public void Bereich_AusgeblendeteReiter_FehlenInDerVorschau()
    {
        var vm = Bereich(
            new TabConfig { Title = "Sichtbar", FolderPath = TabOrdner("S", "a.txt") },
            new TabConfig { Title = "Versteckt", FolderPath = TabOrdner("V", "b.txt"), Hidden = true },
            new TabConfig { Title = "Auch da", FolderPath = TabOrdner("D", "c.txt") });

        vm.RefreshPreview();

        Assert.Equal(2, vm.PreviewLines.Count);
        Assert.DoesNotContain(vm.PreviewLines, z => z.StartsWith("Versteckt", StringComparison.Ordinal));
    }

    [Fact]
    public void Bereich_EinEinzigerReiter_ZeigtDieEintraegeSelbst()
    {
        // „1 Reiter" waere eine wertlose Auskunft — bei nur einem Reiter
        // interessiert der Inhalt.
        var vm = Bereich(new TabConfig
        {
            Title = "Allgemein",
            FolderPath = TabOrdner("Einzeln", "Notiz.txt", "Rechnung.pdf")
        });

        vm.RefreshPreview();

        Assert.Equal(new[] { "Notiz.txt", "Rechnung.pdf" }, vm.PreviewLines.OrderBy(n => n).ToArray());
    }

    [Fact]
    public void Bereich_LeererEinzelReiter_MeldetLeer()
    {
        var vm = Bereich(new TabConfig { Title = "Leer", FolderPath = TabOrdner("Leer") });

        vm.RefreshPreview();

        Assert.True(vm.PreviewEmpty);
    }

    [Fact]
    public void Bereich_NichtGeladeneReiter_LiefernKeineAnzahl()
    {
        // Der teure Fall: Fuer die Anzahl eines nicht geladenen Reiters muesste
        // sein Ordner gelesen werden — bei acht Reitern acht Lesevorgaenge,
        // synchron im Bedienfaden, nur weil die Maus auf der Ueberschrift steht.
        // Deshalb steht die Anzahl nur dort, wo sie ohnehin schon bekannt ist.
        var vm = Bereich(
            new TabConfig { Title = "Erster", FolderPath = TabOrdner("E", "a.txt", "b.txt") },
            new TabConfig { Title = "Zweiter", FolderPath = TabOrdner("Z", "c.txt") },
            new TabConfig { Title = "Dritter", FolderPath = TabOrdner("D", "d.txt") });

        // Nur der aktive Reiter ist geladen.
        Assert.False(vm.Tabs[1].IsLoaded);
        Assert.False(vm.Tabs[2].IsLoaded);

        vm.RefreshPreview();

        Assert.Contains("Zweiter", vm.PreviewLines);
        Assert.Contains("Dritter", vm.PreviewLines);

        // Die ungeladenen Reiter stehen OHNE Klammerzusatz da …
        Assert.DoesNotContain(vm.PreviewLines, z => z.StartsWith("Zweiter (", StringComparison.Ordinal));

        // … und wurden dabei auch nicht geladen.
        Assert.False(vm.Tabs[1].IsLoaded);
        Assert.False(vm.Tabs[2].IsLoaded);
    }

    [Fact]
    public void FreieAnzahl_NichtGeladenUndKaltZwischengespeichert_IstNull()
    {
        var vm = Bereich(
            new TabConfig { Title = "Aktiv", FolderPath = TabOrdner("A", "a.txt") },
            new TabConfig { Title = "Ruht", FolderPath = TabOrdner("R", "b.txt", "c.txt") });

        Assert.Null(vm.Tabs[1].FreieAnzahl);

        // Nach einer Vorschau ist der Zwischenspeicher warm — dann ist sie frei.
        vm.Tabs[1].RefreshPreview();
        Assert.Equal(2, vm.Tabs[1].FreieAnzahl);
    }

    [Fact]
    public void Bereich_OrdnerErscheinenInDerVorschau()
    {
        // Ordner werden in Bereichen angezeigt — dann muessen sie auch in der
        // Vorschau auftauchen.
        var ordner = TabOrdner("MitOrdner", "Datei.txt");
        Directory.CreateDirectory(Path.Combine(ordner, "Projekte"));

        var vm = Bereich(new TabConfig { Title = "Allgemein", FolderPath = ordner });
        vm.RefreshPreview();

        Assert.Contains("Projekte", vm.PreviewLines);
    }
}
