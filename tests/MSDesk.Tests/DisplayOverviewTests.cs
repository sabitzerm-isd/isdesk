using MSDesk.Models;
using MSDesk.Services;

namespace MSDesk.Tests;

/// Aufbereitung der Bildschirm-Konfigurationen fuer die Optionen.
public class DisplayOverviewTests
{
    [Fact]
    public void Describe_EinBildschirm()
    {
        var key = @"\\.\DISPLAY1:0,0,1920,1080";
        Assert.Equal("1 Bildschirm: 1920 × 1080", DisplayOverview.Describe(key));
    }

    [Fact]
    public void Describe_MehrereBildschirme()
    {
        var key = @"\\.\DISPLAY1:0,0,3840,2160|\\.\DISPLAY2:3840,0,1920,1080";
        Assert.Equal("2 Bildschirme: 3840 × 2160, 1920 × 1080", DisplayOverview.Describe(key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("kaputt")]
    public void Describe_UnlesbarerSchluessel_LiefertHinweis(string key)
    {
        Assert.Equal("unbekannt", DisplayOverview.Describe(key));
    }

    [Fact]
    public void SavedConfigurations_ZaehltBereicheJeKonfiguration()
    {
        var laptop = @"\\.\DISPLAY1:0,0,1920,1080";
        var homeoffice = @"\\.\DISPLAY1:0,0,1920,1080|\\.\DISPLAY2:1920,0,2560,1440";

        var config = new AppConfig
        {
            Fences =
            {
                new FenceConfig
                {
                    Title = "A",
                    Layouts =
                    {
                        [laptop] = new LayoutRect { X = 1, Y = 2, Width = 300, Height = 200 },
                        [homeoffice] = new LayoutRect { X = 5, Y = 6, Width = 300, Height = 200 }
                    }
                },
                new FenceConfig
                {
                    Title = "B",
                    Layouts = { [homeoffice] = new LayoutRect { X = 9, Y = 9, Width = 300, Height = 200 } }
                }
            }
        };

        var infos = DisplayOverview.SavedConfigurations(config);

        Assert.Equal(2, infos.Single(i => i.Key == homeoffice).FenceCount);
        Assert.Equal(1, infos.Single(i => i.Key == laptop).FenceCount);
    }

    [Fact]
    public void SavedConfigurations_ZeigtAktuelleKonfiguration_AuchOhneGespeichertesLayout()
    {
        // Frisch: noch nichts gemerkt — die aktive Konfiguration muss trotzdem
        // sichtbar sein, damit nachpruefbar ist, ob gesichert wurde.
        var infos = DisplayOverview.SavedConfigurations(new AppConfig());

        var aktuell = Assert.Single(infos.Where(i => i.IsCurrent));
        Assert.Equal(0, aktuell.FenceCount);
    }
}
