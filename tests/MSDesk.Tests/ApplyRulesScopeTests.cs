using MSDesk.Models;
using MSDesk.Services;
using Xunit;

namespace MSDesk.Tests;

/// Sichert die wichtigste Regel des Aktualisieren-Knopfes ab: Er darf NUR
/// innerhalb der Ablage umsortieren. Andere Bereiche sind tabu — dort hat der
/// Nutzer bewusst einsortiert.
public class ApplyRulesScopeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MSDeskScope_" + Guid.NewGuid().ToString("N"));
    private readonly string _configPath;

    public ApplyRulesScopeTests()
    {
        Directory.CreateDirectory(_root);
        _configPath = Path.Combine(_root, "config.json");
    }

    [Fact]
    public void Aktualisieren_laesst_andere_Bereiche_unberuehrt()
    {
        // Ablage mit Ziel-Tab fuer .sza
        var ablageAllgemein = Path.Combine(_root, "Ablage", "Allgemein");
        var ablageSza = Path.Combine(_root, "Ablage", "SZA");
        // Ein FREMDER Bereich, in dem ebenfalls eine .sza liegt
        var kundenTab = Path.Combine(_root, "Kunden", "Allgemein");
        foreach (var d in new[] { ablageAllgemein, ablageSza, kundenTab }) Directory.CreateDirectory(d);

        var inAblage = Path.Combine(ablageAllgemein, "aus-ablage.sza");
        var beimKunden = Path.Combine(kundenTab, "beim-kunden.sza");
        File.WriteAllText(inAblage, "x");
        File.WriteAllText(beimKunden, "x");

        var config = new ConfigService(_configPath);
        config.Config.Fences.Add(new FenceConfig
        {
            Title = "Ablage",
            Tabs =
            {
                new TabConfig { Title = "Allgemein", FolderPath = ablageAllgemein },
                new TabConfig { Title = "SZA", FolderPath = ablageSza, AutoExtensions = { "sza" } }
            }
        });
        config.Config.Fences.Add(new FenceConfig
        {
            Title = "Kunden",
            Tabs = { new TabConfig { Title = "Allgemein", FolderPath = kundenTab } }
        });

        var sweeper = new DesktopSweeper(config, () => ablageAllgemein);
        sweeper.ApplyRulesEverywhere();

        // In der Ablage wurde umsortiert …
        Assert.False(File.Exists(inAblage));
        Assert.True(File.Exists(Path.Combine(ablageSza, "aus-ablage.sza")));

        // … der fremde Bereich blieb unangetastet.
        Assert.True(File.Exists(beimKunden));
        Assert.False(File.Exists(Path.Combine(ablageSza, "beim-kunden.sza")));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
        catch (Exception) { /* Temp */ }
    }
}
