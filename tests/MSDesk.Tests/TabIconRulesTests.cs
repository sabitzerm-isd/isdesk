using MSDesk.Models;
using MSDesk.Services;

namespace MSDesk.Tests;

/// Automatische Symbol-Zuordnung anhand der Tab-Namen.
public class TabIconRulesTests
{
    [Theory]
    [InlineData("Verwaltung", "verwaltung.png")]
    [InlineData("Kunden", "kunde.png")]
    [InlineData("PDF-Dokumente", "pdf.png")]
    [InlineData("Importiert", "import.png")]
    [InlineData("Netzwerk & Server", "netzwerk.png")]
    [InlineData("Büro", "verwaltung.png")]          // Umlaut wird vereinheitlicht
    [InlineData("BUERO", "verwaltung.png")]         // Gross-/Kleinschreibung egal
    [InlineData("Fotos", "foto.png")]
    [InlineData("Passwörter", "sicherheit.png")]
    public void Suggest_ErkenntPassendesSymbol(string tabName, string erwartet)
    {
        Assert.Equal(erwartet, TabIconRules.Suggest(tabName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Xyzzy Quux")]
    public void Suggest_OhneTreffer_LiefertNull(string? tabName)
    {
        Assert.Null(TabIconRules.Suggest(tabName));
    }

    [Fact]
    public void Suggest_SpeziellerBegriffGewinntVorAllgemeinem()
    {
        // "lesezeichenleiste" steht vor "lesezeichen" — sonst wuerde die
        // allgemeine Regel die speziellere verdecken.
        Assert.Equal("lesezeichenleiste.png", TabIconRules.Suggest("Lesezeichenleiste"));
        Assert.Equal("web.png", TabIconRules.Suggest("Lesezeichen"));
    }

    [Fact]
    public void ApplyMissing_SetztNurLeereSymbole()
    {
        var config = new AppConfig
        {
            Fences =
            {
                new FenceConfig
                {
                    Title = "Test",
                    Tabs =
                    {
                        new TabConfig { Title = "Verwaltung" },                            // leer → wird gesetzt
                        new TabConfig { Title = "Kunden", IconPath = "eigenes.png" },      // gesetzt → bleibt
                        new TabConfig { Title = "Xyzzy" }                                  // keine Regel → bleibt leer
                    }
                }
            }
        };

        var geaendert = TabIconRules.ApplyMissing(config);

        var tabs = config.Fences[0].Tabs;
        Assert.Equal(1, geaendert);
        Assert.Equal("verwaltung.png", tabs[0].IconPath);
        Assert.Equal("eigenes.png", tabs[1].IconPath);   // von Hand gesetzt bleibt unangetastet
        Assert.Null(tabs[2].IconPath);
    }

    [Fact]
    public void ApplyMissing_IstWiederholbar_OhneWeitereAenderungen()
    {
        var config = new AppConfig
        {
            Fences = { new FenceConfig { Title = "Test", Tabs = { new TabConfig { Title = "Archiv" } } } }
        };

        Assert.Equal(1, TabIconRules.ApplyMissing(config));
        Assert.Equal(0, TabIconRules.ApplyMissing(config)); // zweiter Lauf aendert nichts mehr
    }
}
