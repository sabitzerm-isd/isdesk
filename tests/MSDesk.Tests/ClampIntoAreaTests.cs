using MSDesk.Models;
using MSDesk.Services;

namespace MSDesk.Tests;

/// <summary>
/// Zurueckholen eines Bereichs in den sichtbaren Bildschirmbereich.
/// Kernanforderung: mehrere zurueckgeholte Bereiche duerfen sich NICHT auf
/// demselben Punkt stapeln — genau das passierte mit dem festen Punkt (100/100).
/// </summary>
public class ClampIntoAreaTests
{
    private static readonly LayoutTransfer.Area Laptop = new(0, 0, 1707, 1040);

    [Fact]
    public void SichtbarerBereichBleibtUnveraendert()
    {
        var item = new LayoutRect { X = 300, Y = 200, Width = 400, Height = 300 };
        var ergebnis = LayoutTransfer.ClampIntoArea(item, Laptop);

        Assert.Equal(300, ergebnis.X);
        Assert.Equal(200, ergebnis.Y);
    }

    [Fact]
    public void TeilweiseSichtbarerBereichBleibtUnveraendert()
    {
        // Ragt rechts hinaus, ist aber noch gut greifbar.
        var item = new LayoutRect { X = 1500, Y = 200, Width = 400, Height = 300 };
        var ergebnis = LayoutTransfer.ClampIntoArea(item, Laptop);

        Assert.Equal(1500, ergebnis.X);
    }

    [Fact]
    public void KomplettAusserhalbWirdHineingeschoben()
    {
        var item = new LayoutRect { X = 4000, Y = 200, Width = 400, Height = 300 };
        var ergebnis = LayoutTransfer.ClampIntoArea(item, Laptop);

        Assert.True(ergebnis.X + ergebnis.Width <= Laptop.Width + 0.5);
        Assert.True(ergebnis.X >= 0);
    }

    [Fact]
    public void MehrereBereicheStapelnSichNichtAufDemselbenPunkt()
    {
        // Alle lagen auf dem entfallenen zweiten Monitor.
        var draussen = new[]
        {
            new LayoutRect { X = 3500, Y = 100, Width = 300, Height = 200 },
            new LayoutRect { X = 3500, Y = 400, Width = 300, Height = 200 },
            new LayoutRect { X = 3500, Y = 700, Width = 300, Height = 200 },
        };

        var ergebnis = draussen.Select(r => LayoutTransfer.ClampIntoArea(r, Laptop)).ToList();

        // Die Y-Lage bleibt erhalten — sie liegen also NICHT alle aufeinander.
        Assert.Equal(3, ergebnis.Select(r => (r.X, r.Y)).Distinct().Count());
        Assert.Equal(100, ergebnis[0].Y);
        Assert.Equal(400, ergebnis[1].Y);
        Assert.Equal(700, ergebnis[2].Y);
    }

    [Fact]
    public void UeberbreiterBereichBeginntAmRand()
    {
        var item = new LayoutRect { X = 5000, Y = 50, Width = 3000, Height = 300 };
        var ergebnis = LayoutTransfer.ClampIntoArea(item, Laptop);

        Assert.Equal(Laptop.X, ergebnis.X);
    }

    [Fact]
    public void OhneBrauchbareFlaecheBleibtAllesUnveraendert()
    {
        var item = new LayoutRect { X = 4000, Y = 200, Width = 400, Height = 300 };
        var ergebnis = LayoutTransfer.ClampIntoArea(item, new LayoutTransfer.Area(0, 0, 0, 0));

        Assert.Equal(4000, ergebnis.X);
    }
}
