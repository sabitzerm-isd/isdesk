using System.IO;
using MSDesk.Models;
using MSDesk.Services;

namespace MSDesk.Tests;

/// <summary>
/// Der Ordner der Bereiche lag frueher fest auf „D:\Fences". Auf einem Rechner
/// ohne dieses Laufwerk startete MSDesk dadurch scheinbar gar nicht — der
/// Blocker fuer die Weitergabe an Kollegen.
///
/// Zwei Dinge muessen stimmen: es muss immer ein benutzbarer Ort herauskommen,
/// und beim Umzug muessen die ABSOLUTEN Tab-Pfade mitwandern.
/// </summary>
public class BaseFolderResolverTests : IDisposable
{
    private readonly string _root;

    public BaseFolderResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msdesk_base_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { /* egal */ }
    }

    [Fact]
    public void EnsureUsable_LeererWunsch_LiefertBenutzbarenOrdner()
    {
        var ordner = BaseFolderResolver.EnsureUsable(null);

        Assert.False(string.IsNullOrWhiteSpace(ordner));
        Assert.True(Directory.Exists(ordner));
    }

    [Fact]
    public void EnsureUsable_VorhandenerOrdner_BleibtUnveraendert()
    {
        // Der Regelfall auf dem Rechner des Anwenders: der eingetragene Ordner
        // existiert. Dann darf sich NICHTS aendern.
        var vorhanden = Path.Combine(_root, "Fences");
        Directory.CreateDirectory(vorhanden);

        Assert.Equal(vorhanden, BaseFolderResolver.EnsureUsable(vorhanden));
    }

    [Fact]
    public void EnsureUsable_FehlendesLaufwerk_WeichtAus()
    {
        // Ein Laufwerksbuchstabe, den es sicher nicht gibt.
        var frei = FreierLaufwerksbuchstabe();
        Assert.NotNull(frei);

        var ergebnis = BaseFolderResolver.EnsureUsable($@"{frei}:\Fences");

        Assert.NotEqual($@"{frei}:\Fences", ergebnis);
        Assert.True(Directory.Exists(ergebnis));
    }

    [Fact]
    public void EnsureUsable_VorhandenerOrdner_SchreibtNichtHinein()
    {
        // Der Schnellpfad muss greifen: bei jedem Start eine Probe-Datei
        // anzulegen waere unnoetige Last — und in einem Cloud-Ordner loeste es
        // jedes Mal einen Abgleich aus.
        var vorhanden = Path.Combine(_root, "Fences");
        Directory.CreateDirectory(vorhanden);

        BaseFolderResolver.EnsureUsable(vorhanden);

        Assert.Empty(Directory.GetFileSystemEntries(vorhanden));
    }

    [Fact]
    public void Remap_SchreibtTabOrdnerUm()
    {
        var config = new AppConfig { BaseFolder = @"D:\Fences" };
        config.Fences.Add(new FenceConfig
        {
            Title = "Support",
            Tabs =
            {
                new TabConfig { Title = "Allgemein", FolderPath = @"D:\Fences\Support\Allgemein" },
                new TabConfig { Title = "Trac",      FolderPath = @"D:\Fences\Support\Trac" },
            }
        });

        var anzahl = BaseFolderResolver.Remap(config, @"D:\Fences", @"C:\Users\Test\Fences");

        Assert.Equal(2, anzahl);
        Assert.Equal(@"C:\Users\Test\Fences\Support\Allgemein", config.Fences[0].Tabs[0].FolderPath);
        Assert.Equal(@"C:\Users\Test\Fences\Support\Trac", config.Fences[0].Tabs[1].FolderPath);
    }

    [Fact]
    public void Remap_NimmtBeidePlatzGedaechtnisseMit()
    {
        var config = new AppConfig { BaseFolder = @"D:\Fences" };
        config.Placements["camtasia.lnk"] = @"D:\Fences\Programme\Video";
        config.TargetPlacements[@"c:\program files\camtasia\camtasia.exe"] = @"D:\Fences\Programme\Video";

        BaseFolderResolver.Remap(config, @"D:\Fences", @"E:\Fences");

        Assert.Equal(@"E:\Fences\Programme\Video", config.Placements["camtasia.lnk"]);
        Assert.Equal(@"E:\Fences\Programme\Video",
                     config.TargetPlacements[@"c:\program files\camtasia\camtasia.exe"]);
    }

    [Fact]
    public void Remap_FremdePfadeBleibenUnangetastet()
    {
        // Ein Tab kann auf einen Ordner AUSSERHALB des Basisordners zeigen
        // (z. B. ein Netzlaufwerk). Der darf nicht mit umgebogen werden.
        var config = new AppConfig { BaseFolder = @"D:\Fences" };
        config.Fences.Add(new FenceConfig
        {
            Tabs =
            {
                new TabConfig { FolderPath = @"D:\Fences\A\B" },
                new TabConfig { FolderPath = @"\\server\freigabe\Projekte" },
                new TabConfig { FolderPath = @"D:\FencesAlt\A" }, // aehnlicher Anfang, anderer Ordner
            }
        });

        var anzahl = BaseFolderResolver.Remap(config, @"D:\Fences", @"E:\Fences");

        Assert.Equal(1, anzahl);
        Assert.Equal(@"\\server\freigabe\Projekte", config.Fences[0].Tabs[1].FolderPath);
        Assert.Equal(@"D:\FencesAlt\A", config.Fences[0].Tabs[2].FolderPath);
    }

    [Fact]
    public void Remap_GleicherOrdner_TutNichts()
    {
        var config = new AppConfig { BaseFolder = @"D:\Fences" };
        config.Fences.Add(new FenceConfig { Tabs = { new TabConfig { FolderPath = @"D:\Fences\A" } } });

        Assert.Equal(0, BaseFolderResolver.Remap(config, @"D:\Fences", @"d:\fences\"));
        Assert.Equal(@"D:\Fences\A", config.Fences[0].Tabs[0].FolderPath);
    }

    [Fact]
    public void Remap_LeereAngaben_TutNichts()
    {
        var config = new AppConfig();
        config.Fences.Add(new FenceConfig { Tabs = { new TabConfig { FolderPath = @"D:\Fences\A" } } });

        Assert.Equal(0, BaseFolderResolver.Remap(config, "", @"E:\Fences"));
        Assert.Equal(0, BaseFolderResolver.Remap(config, @"D:\Fences", ""));
    }

    [Fact]
    public void MoveTo_ZielHatGleichnamigenOrdner_LehntAb()
    {
        // Sonst zeigten die Bereiche danach auf den FREMDEN Inhalt am Ziel,
        // waehrend die echten Dateien unsichtbar am alten Ort liegenblieben.
        var alt = Path.Combine(_root, "Alt");
        var neu = Path.Combine(_root, "Neu");
        Directory.CreateDirectory(Path.Combine(alt, "Ablage"));
        Directory.CreateDirectory(Path.Combine(neu, "Ablage")); // schon belegt
        File.WriteAllText(Path.Combine(alt, "Ablage", "wichtig.txt"), "Nutzdaten");

        var config = new AppConfig { BaseFolder = alt };
        config.Fences.Add(new FenceConfig
        {
            Tabs = { new TabConfig { FolderPath = Path.Combine(alt, "Ablage") } }
        });

        var ergebnis = BaseFolderResolver.MoveTo(config, neu);

        Assert.False(ergebnis.Erfolg);
        Assert.Contains("Ablage", ergebnis.Fehler);

        // Nichts angefasst: Datei am Platz, Einstellungen unveraendert.
        Assert.True(File.Exists(Path.Combine(alt, "Ablage", "wichtig.txt")));
        Assert.Equal(alt, config.BaseFolder);
        Assert.Equal(Path.Combine(alt, "Ablage"), config.Fences[0].Tabs[0].FolderPath);
    }

    [Fact]
    public void MoveTo_LeeresZiel_VerschiebtUndSchreibtPfadeUm()
    {
        var alt = Path.Combine(_root, "Alt");
        var neu = Path.Combine(_root, "Neu");
        Directory.CreateDirectory(Path.Combine(alt, "Programme"));
        File.WriteAllText(Path.Combine(alt, "Programme", "Word.lnk"), "x");

        var config = new AppConfig { BaseFolder = alt };
        config.Fences.Add(new FenceConfig
        {
            Tabs = { new TabConfig { FolderPath = Path.Combine(alt, "Programme") } }
        });

        var ergebnis = BaseFolderResolver.MoveTo(config, neu);

        Assert.True(ergebnis.Erfolg);
        Assert.Equal(neu, config.BaseFolder);
        Assert.Equal(Path.Combine(neu, "Programme"), config.Fences[0].Tabs[0].FolderPath);
        Assert.True(File.Exists(Path.Combine(neu, "Programme", "Word.lnk")));
        Assert.False(Directory.Exists(Path.Combine(alt, "Programme")));
    }

    [Fact]
    public void MoveTo_ZielInnerhalbDerQuelle_LehntAb()
    {
        var alt = Path.Combine(_root, "Alt");
        Directory.CreateDirectory(alt);

        var config = new AppConfig { BaseFolder = alt };
        var ergebnis = BaseFolderResolver.MoveTo(config, Path.Combine(alt, "Unterordner"));

        Assert.False(ergebnis.Erfolg);
        Assert.Equal(alt, config.BaseFolder);
    }

    [Fact]
    public void MoveTo_GleicherOrdner_IstErfolgOhneAenderung()
    {
        var alt = Path.Combine(_root, "Alt");
        Directory.CreateDirectory(alt);

        var config = new AppConfig { BaseFolder = alt };
        var ergebnis = BaseFolderResolver.MoveTo(config, alt);

        Assert.True(ergebnis.Erfolg);
        Assert.Equal(0, ergebnis.Pfade);
    }

    /// Sucht einen Laufwerksbuchstaben, der auf diesem Rechner nicht vergeben ist.
    private static char? FreierLaufwerksbuchstabe()
    {
        var belegt = DriveInfo.GetDrives()
            .Select(d => char.ToUpperInvariant(d.Name[0]))
            .ToHashSet();

        for (var c = 'Z'; c >= 'E'; c--)
            if (!belegt.Contains(c)) return c;

        return null;
    }
}
