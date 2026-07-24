using MSDesk.Models;
using MSDesk.Services;

namespace MSDesk.Tests;

/// Anteiliges Uebertragen der Anordnung auf eine unbekannte Bildschirm-Konfiguration.
public class LayoutTransferTests
{
    private static readonly LayoutTransfer.Area ZweiMonitore = new(0, 0, 3840, 1080);
    private static readonly LayoutTransfer.Area NurLaptop = new(0, 0, 1920, 1080);

    [Fact]
    public void Map_BehaeltDieRelativeLage()
    {
        // Bereich am linken Rand der Doppel-Anordnung …
        var links = new LayoutRect { X = 0, Y = 0, Width = 400, Height = 300 };
        var abgebildet = LayoutTransfer.Map(links, ZweiMonitore, NurLaptop);

        // … liegt danach wieder am linken Rand.
        Assert.Equal(0, abgebildet.X);
        Assert.Equal(0, abgebildet.Y);
    }

    [Fact]
    public void Map_RechterBereichBleibtRechts()
    {
        // Bereich auf dem zweiten Monitor (rechte Haelfte)
        var rechts = new LayoutRect { X = 2880, Y = 100, Width = 400, Height = 300 };
        var abgebildet = LayoutTransfer.Map(rechts, ZweiMonitore, NurLaptop);

        // 2880 von 3840 = 75 % → auf 1920 sind das 1440
        Assert.Equal(1440, abgebildet.X);
        Assert.True(abgebildet.X > NurLaptop.Width / 2, "Der rechte Bereich muss rechts bleiben.");
    }

    [Fact]
    public void Map_HaeltAllesInnerhalbDerZielflaeche()
    {
        var ganzRechts = new LayoutRect { X = 3700, Y = 900, Width = 400, Height = 300 };
        var abgebildet = LayoutTransfer.Map(ganzRechts, ZweiMonitore, NurLaptop);

        Assert.True(abgebildet.X >= 0);
        Assert.True(abgebildet.Y >= 0);
        Assert.True(abgebildet.X + abgebildet.Width <= NurLaptop.Width);
        Assert.True(abgebildet.Y + abgebildet.Height <= NurLaptop.Height);
    }

    [Fact]
    public void Map_WahrtDieMindestgroesse()
    {
        var winzig = new LayoutRect { X = 0, Y = 0, Width = 200, Height = 130 };
        var abgebildet = LayoutTransfer.Map(winzig, ZweiMonitore, NurLaptop);

        Assert.True(abgebildet.Width >= LayoutTransfer.MinWidth);
        Assert.True(abgebildet.Height >= LayoutTransfer.MinHeight);
    }

    [Fact]
    public void Map_SkaliertDieGroesseEinheitlich_KeineVerzerrung()
    {
        // Von zwei Monitoren auf einen: Breite halbiert sich, Hoehe bleibt.
        // Die Groesse darf trotzdem NICHT verzerrt werden.
        var quadratisch = new LayoutRect { X = 200, Y = 200, Width = 400, Height = 400 };
        var abgebildet = LayoutTransfer.Map(quadratisch, ZweiMonitore, NurLaptop);

        Assert.Equal(abgebildet.Width, abgebildet.Height);
    }

    [Fact]
    public void Map_OhneBrauchbareFlaeche_BleibtUnveraendert()
    {
        var item = new LayoutRect { X = 10, Y = 20, Width = 300, Height = 200 };
        var abgebildet = LayoutTransfer.Map(item, new LayoutTransfer.Area(0, 0, 0, 0), NurLaptop);

        Assert.Equal(10, abgebildet.X);
        Assert.Equal(20, abgebildet.Y);
        Assert.Equal(300, abgebildet.Width);
    }

    [Fact]
    public void Map_MehrereBereiche_BehaltenIhreReihenfolge()
    {
        var a = new LayoutRect { X = 100, Y = 0, Width = 300, Height = 300 };
        var b = new LayoutRect { X = 1400, Y = 0, Width = 300, Height = 300 };
        var c = new LayoutRect { X = 3000, Y = 0, Width = 300, Height = 300 };

        var na = LayoutTransfer.Map(a, ZweiMonitore, NurLaptop);
        var nb = LayoutTransfer.Map(b, ZweiMonitore, NurLaptop);
        var nc = LayoutTransfer.Map(c, ZweiMonitore, NurLaptop);

        Assert.True(na.X < nb.X, "Die Anordnung von links nach rechts muss erhalten bleiben.");
        Assert.True(nb.X < nc.X, "Die Anordnung von links nach rechts muss erhalten bleiben.");
    }
}
