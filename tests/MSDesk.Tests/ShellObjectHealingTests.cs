using System.IO;
using MSDesk.Models;
using MSDesk.Services;

namespace MSDesk.Tests;

/// <summary>
/// Versehentlich geloeschte Systemobjekte kommen zurueck — und NUR die.
///
/// Der Papierkorb in einem Bereich ist ein Ordner mit angehaengter Kennung.
/// Wird er geloescht, laesst er sich gefahrlos neu anlegen: es entsteht kein
/// Inhalt und es geht nichts verloren. Bei einer Verknuepfung oder einer echten
/// Datei gilt das ausdruecklich NICHT — deren Inhalt kann MSDesk nicht kennen.
/// </summary>
public class ShellObjectHealingTests : IDisposable
{
    private const string Papierkorb = "645FF040-5081-101B-9F08-00AA002F954E";

    private readonly string _root;
    private readonly string _tab;
    private readonly ConfigService _config;

    public ShellObjectHealingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msdesk_heal_" + Guid.NewGuid().ToString("N")[..8]);
        _tab = Path.Combine(_root, "Papierkorb", "Allgemein");
        Directory.CreateDirectory(_tab);

        _config = new ConfigService(Path.Combine(_root, "config.json"));
        _config.Config.Fences.Add(new FenceConfig
        {
            Title = "Papierkorb",
            Tabs = { new TabConfig { Title = "Allgemein", FolderPath = _tab } }
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { /* egal */ }
    }

    [Fact]
    public void GeloeschtesSystemobjekt_WirdWiederAngelegt()
    {
        _config.Config.Placements[$"papierkorb.{{{Papierkorb.ToLowerInvariant()}}}"] = _tab;

        Assert.Equal(1, ShellObjectHealing.Heilen(_config));

        var wieder = Directory.GetDirectories(_tab);
        Assert.Single(wieder);
        Assert.Contains(Papierkorb, Path.GetFileName(wieder[0]), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Name_WirdGrossGeschrieben()
    {
        // Das Gedaechtnis speichert klein; angezeigt wird der Teil vor der
        // Kennung. Ohne Grossschreibung hiesse er danach „papierkorb".
        _config.Config.Placements[$"papierkorb.{{{Papierkorb.ToLowerInvariant()}}}"] = _tab;
        ShellObjectHealing.Heilen(_config);

        var name = Path.GetFileName(Directory.GetDirectories(_tab)[0]);
        Assert.StartsWith("Papierkorb.{", name, StringComparison.Ordinal);
    }

    [Fact]
    public void SchonVorhanden_WirdNichtVerdoppelt()
    {
        Directory.CreateDirectory(Path.Combine(_tab, $"Papierkorb.{{{Papierkorb}}}"));
        _config.Config.Placements[$"papierkorb.{{{Papierkorb.ToLowerInvariant()}}}"] = _tab;

        Assert.Equal(0, ShellObjectHealing.Heilen(_config));
        Assert.Single(Directory.GetDirectories(_tab));
    }

    [Fact]
    public void GewoehnlicheVerknuepfung_WirdNIENachgebaut()
    {
        // Die wichtigste Grenze: Der Inhalt einer Verknuepfung ist nicht
        // bekannt. Etwas Leeres vorzugaukeln waere schlimmer als die Luecke.
        _config.Config.Placements["camtasia.lnk"] = _tab;

        Assert.Equal(0, ShellObjectHealing.Heilen(_config));
        Assert.Empty(Directory.GetFileSystemEntries(_tab));
    }

    [Fact]
    public void FehlenderZielordner_WirdUebersprungen()
    {
        _config.Config.Placements[$"papierkorb.{{{Papierkorb.ToLowerInvariant()}}}"] =
            Path.Combine(_root, "gibtesnicht");

        Assert.Equal(0, ShellObjectHealing.Heilen(_config));
    }

    [Fact]
    public void MehrereSystemobjekte_KommenAlleZurueck()
    {
        const string DieserPc = "20D04FE0-3AEA-1069-A2D8-08002B30309D";
        _config.Config.Placements[$"papierkorb.{{{Papierkorb.ToLowerInvariant()}}}"] = _tab;
        _config.Config.Placements[$"dieser pc.{{{DieserPc.ToLowerInvariant()}}}"] = _tab;

        Assert.Equal(2, ShellObjectHealing.Heilen(_config));
        Assert.Equal(2, Directory.GetDirectories(_tab).Length);
    }
}
