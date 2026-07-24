using MSDesk.Models;
using MSDesk.Services;

namespace MSDesk.Tests;

/// <summary>
/// Die Kennung einer Bildschirm-Konfiguration muss stabil bleiben, wenn Windows
/// beim Wiederanstecken die Geraetenamen neu vergibt (\\.\DISPLAY1 → DISPLAY5).
/// Sonst gilt dieselbe Anordnung als unbekannt und wird nicht wiederhergestellt.
/// </summary>
public class DisplayConfigKeyTests
{
    [Fact]
    public void GeraetenameAendertDieKennungNicht()
    {
        var vorher = @"\\.\DISPLAY1:0,0,3440,1440|\\.\DISPLAY2:3440,172,2560,1600";
        var nachher = @"\\.\DISPLAY5:0,0,3440,1440|\\.\DISPLAY1:3440,172,2560,1600";

        Assert.Equal(DisplayConfig.Normalize(vorher), DisplayConfig.Normalize(nachher));
    }

    [Fact]
    public void ReihenfolgeDerBildschirmeAendertDieKennungNicht()
    {
        var a = "3440x1440@0,0|2560x1600@3440,172";
        var b = "2560x1600@3440,172|3440x1440@0,0";

        Assert.Equal(DisplayConfig.Normalize(a), DisplayConfig.Normalize(b));
    }

    [Fact]
    public void AlteUndNeueFormErgebenDieselbeKennung()
    {
        var alt = @"\\.\DISPLAY1:0,0,1920,1080";
        var neu = "1920x1080@0,0";

        Assert.Equal(DisplayConfig.Normalize(neu), DisplayConfig.Normalize(alt));
    }

    [Fact]
    public void UnterschiedlicheAufloesungBleibtUnterscheidbar()
    {
        var laptop = DisplayConfig.Normalize(@"\\.\DISPLAY1:0,0,1920,1080");
        var beide = DisplayConfig.Normalize(@"\\.\DISPLAY1:0,0,1920,1080|\\.\DISPLAY2:1920,0,2560,1440");

        Assert.NotEqual(laptop, beide);
    }

    [Fact]
    public void UnlesbareKennungBleibtUnveraendert()
    {
        Assert.Equal("kaputt", DisplayConfig.Normalize("kaputt"));
    }

    [Fact]
    public void MigrateKeys_RettetGespeicherteAnordnungen()
    {
        var alterSchluessel = @"\\.\DISPLAY1:0,0,3440,1440|\\.\DISPLAY2:3440,172,2560,1600";
        var config = new AppConfig
        {
            Fences =
            {
                new FenceConfig
                {
                    Title = "A",
                    Layouts = { [alterSchluessel] = new LayoutRect { X = 5, Y = 6, Width = 300, Height = 200 } }
                }
            },
            DisplayNames = { [alterSchluessel] = "Homeoffice" },
            DisplayAreas = { [alterSchluessel] = new LayoutRect { X = 0, Y = 0, Width = 6000, Height = 1600 } }
        };

        var geaendert = DisplayConfig.MigrateKeys(config);
        var neuerSchluessel = DisplayConfig.Normalize(alterSchluessel);

        Assert.Equal(3, geaendert);
        Assert.True(config.Fences[0].Layouts.ContainsKey(neuerSchluessel));
        Assert.Equal(5, config.Fences[0].Layouts[neuerSchluessel].X);
        Assert.Equal("Homeoffice", config.DisplayNames[neuerSchluessel]);
        Assert.True(config.DisplayAreas.ContainsKey(neuerSchluessel));
    }

    [Fact]
    public void MigrateKeys_IstWiederholbar()
    {
        var config = new AppConfig
        {
            Fences =
            {
                new FenceConfig
                {
                    Title = "A",
                    Layouts = { [@"\\.\DISPLAY1:0,0,1920,1080"] = new LayoutRect { Width = 300, Height = 200 } }
                }
            }
        };

        Assert.Equal(1, DisplayConfig.MigrateKeys(config));
        Assert.Equal(0, DisplayConfig.MigrateKeys(config)); // zweiter Lauf aendert nichts
    }

    [Fact]
    public void Describe_VerstehtBeideFormen()
    {
        Assert.Equal("1 Bildschirm: 1920 × 1080", DisplayOverview.Describe("1920x1080@0,0"));
        Assert.Equal("1 Bildschirm: 1920 × 1080", DisplayOverview.Describe(@"\\.\DISPLAY1:0,0,1920,1080"));
    }
}
