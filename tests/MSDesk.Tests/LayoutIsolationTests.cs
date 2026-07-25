using System.IO;
using MSDesk.Models;
using MSDesk.ViewModels;

namespace MSDesk.Tests;

/// <summary>
/// Sichert ab, dass das blosse Verschieben eines Bereichs NICHT in die
/// gespeicherten Bildschirm-Anordnungen schreibt.
///
/// Hintergrund: Eine Hilfsmethode im ViewModel tat genau das — bei jeder
/// einzelnen Aenderung von X, Y, Breite oder Hoehe, mit der gerade gueltigen
/// Kennung. Dadurch landeten beim Anstecken eines Monitors die
/// Zwischenpositionen, die Windows selbst setzt, sofort in der Anordnung —
/// teils unter der alten, teils unter der neuen Kennung. Es entstanden
/// Anordnungen, die X aus der einen und Y aus der anderen Konfiguration trugen.
///
/// Gespeichert wird ausschliesslich zentral vom FenceManager: geprueft,
/// gebuendelt und nie waehrend eines Bildschirmwechsels.
/// </summary>
public class LayoutIsolationTests
{
    private static FenceViewModel Build(out FenceConfig config)
    {
        config = new FenceConfig
        {
            Title = "Test",
            X = 100, Y = 200, Width = 400, Height = 300,
            Layouts =
            {
                ["3440x1440@0,0|2560x1600@3440,172"] = new LayoutRect
                    { X = 2840, Y = 1180, Width = 554, Height = 189 },
                ["2560x1600@0,0"] = new LayoutRect
                    { X = 1940, Y = 1180, Width = 554, Height = 189 },
            }
        };
        return new FenceViewModel(config, Path.GetTempPath());
    }

    [Fact]
    public void Verschieben_AendertKeineGespeicherteAnordnung()
    {
        var vm = Build(out var config);

        vm.X = 1234;
        vm.Y = 5678;

        var doppel = config.Layouts["3440x1440@0,0|2560x1600@3440,172"];
        var laptop = config.Layouts["2560x1600@0,0"];

        Assert.Equal(2840, doppel.X);
        Assert.Equal(1180, doppel.Y);
        Assert.Equal(1940, laptop.X);
        Assert.Equal(1180, laptop.Y);
    }

    [Fact]
    public void GroesseAendern_AendertKeineGespeicherteAnordnung()
    {
        var vm = Build(out var config);

        vm.Width = 999;
        vm.Height = 888;

        foreach (var eintrag in config.Layouts.Values)
        {
            Assert.Equal(554, eintrag.Width);
            Assert.Equal(189, eintrag.Height);
        }
    }

    [Fact]
    public void Verschieben_LegtKeineNeueKennungAn()
    {
        var vm = Build(out var config);
        var vorher = config.Layouts.Count;

        vm.X = 1;
        vm.Y = 2;
        vm.Width = 300;
        vm.Height = 200;

        // Frueher entstand hier ein Eintrag unter der Kennung des Testrechners.
        Assert.Equal(vorher, config.Layouts.Count);
    }

    [Fact]
    public void Verschieben_HaeltDenAktuellenStandFest()
    {
        // Die allgemeinen Felder sollen sehr wohl mitlaufen — nur eben die
        // gespeicherten Anordnungen nicht.
        var vm = Build(out var config);

        vm.X = 777;
        vm.Y = 555;

        Assert.Equal(777, config.X);
        Assert.Equal(555, config.Y);
    }
}
