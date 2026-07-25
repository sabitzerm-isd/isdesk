using MSDesk.Models;
using MSDesk.Services;

namespace MSDesk.Tests;

/// <summary>
/// Das automatische Anordnen muss DREI Dinge gleichzeitig leisten:
///   1. keine Ueberschneidungen,
///   2. die gewohnte Anordnung bleibt erkennbar (Nachbarschaften, links/rechts,
///      oben/unten),
///   3. es wird NICHT verschoben, wenn alles ohnehin passt (Praesentationsfall:
///      ein Beamer kommt dazu).
/// </summary>
public class LayoutSimilarityTests
{
    // Nachbildung der tatsaechlichen Lage: linker Ultrawide + rechter Monitor.
    private static readonly LayoutTransfer.Area ZweiMonitore = new(0, 0, 6000, 1440);
    private static readonly LayoutTransfer.Area NurLaptop = new(0, 0, 1707, 1040);

    /// Bereiche wie im echten Aufbau: einer links, der Rest rechts gruppiert.
    private static List<LayoutRect> EchteAnordnung() => new()
    {
        /* 0 Lesezeichen */ new LayoutRect { X = 20,   Y = 20,   Width = 430, Height = 430 },
        /* 1 ISD         */ new LayoutRect { X = 3620, Y = 250,  Width = 460, Height = 145 },
        /* 2 Support     */ new LayoutRect { X = 4110, Y = 250,  Width = 380, Height = 145 },
        /* 3 EigeneProg  */ new LayoutRect { X = 3620, Y = 420,  Width = 460, Height = 150 },
        /* 4 KI          */ new LayoutRect { X = 4110, Y = 420,  Width = 380, Height = 150 },
        /* 5 Kunden      */ new LayoutRect { X = 3620, Y = 600,  Width = 460, Height = 150 },
        /* 6 Papierkorb  */ new LayoutRect { X = 4110, Y = 600,  Width = 200, Height = 150 },
        /* 7 Cloud       */ new LayoutRect { X = 3620, Y = 780,  Width = 460, Height = 150 },
        /* 8 Ablage      */ new LayoutRect { X = 3100, Y = 960,  Width = 560, Height = 190 },
        /* 9 Launcher    */ new LayoutRect { X = 3700, Y = 960,  Width = 200, Height = 190 },
        /*10 Programme   */ new LayoutRect { X = 3940, Y = 960,  Width = 560, Height = 190 },
    };

    private static bool Ueberschneidet(LayoutRect a, LayoutRect b)
        => a.X < b.X + b.Width && b.X < a.X + a.Width
        && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    private static void KeineUeberschneidungen(IReadOnlyList<LayoutRect> rects)
    {
        for (var i = 0; i < rects.Count; i++)
        for (var j = i + 1; j < rects.Count; j++)
            Assert.False(Ueberschneidet(rects[i], rects[j]),
                $"Bereich {i} und {j} überschneiden sich: " +
                $"({rects[i].X}/{rects[i].Y} {rects[i].Width}×{rects[i].Height}) und " +
                $"({rects[j].X}/{rects[j].Y} {rects[j].Width}×{rects[j].Height})");
    }

    [Fact]
    public void Anordnen_ErzeugtKeineUeberschneidungen()
    {
        var ergebnis = LayoutTransfer.Arrange(EchteAnordnung(), ZweiMonitore, NurLaptop);
        KeineUeberschneidungen(ergebnis);
    }

    [Fact]
    public void Anordnen_AllesInnerhalbDesBildschirms()
    {
        var ergebnis = LayoutTransfer.Arrange(EchteAnordnung(), ZweiMonitore, NurLaptop);

        foreach (var r in ergebnis)
        {
            Assert.True(r.X >= NurLaptop.X - 0.5, $"links heraus: {r.X}");
            Assert.True(r.Y >= NurLaptop.Y - 0.5, $"oben heraus: {r.Y}");
            Assert.True(r.X + r.Width <= NurLaptop.X + NurLaptop.Width + 0.5, $"rechts heraus: {r.X + r.Width}");
            Assert.True(r.Y + r.Height <= NurLaptop.Y + NurLaptop.Height + 0.5, $"unten heraus: {r.Y + r.Height}");
        }
    }

    [Fact]
    public void Anordnen_BehaeltDieLinksRechtsBeziehung()
    {
        var vorher = EchteAnordnung();
        var nachher = LayoutTransfer.Arrange(vorher, ZweiMonitore, NurLaptop);

        // ISD (1) lag links von Support (2) — das muss so bleiben.
        Assert.True(Mitte(nachher[1]).X < Mitte(nachher[2]).X,
            "ISD muss links von Support bleiben.");
        // Kunden (5) links von Papierkorb (6)
        Assert.True(Mitte(nachher[5]).X < Mitte(nachher[6]).X,
            "Kunden muss links von Papierkorb bleiben.");
        // Ablage (8) links von Programme (10)
        Assert.True(Mitte(nachher[8]).X < Mitte(nachher[10]).X,
            "Ablage muss links von Programme bleiben.");
    }

    [Fact]
    public void Anordnen_BehaeltDieObenUntenBeziehung()
    {
        var nachher = LayoutTransfer.Arrange(EchteAnordnung(), ZweiMonitore, NurLaptop);

        // Die rechte Spalte war von oben nach unten: ISD, EigeneProg, Kunden, Cloud, Programme
        Assert.True(Mitte(nachher[1]).Y < Mitte(nachher[3]).Y, "ISD über Eigene Programme");
        Assert.True(Mitte(nachher[3]).Y < Mitte(nachher[5]).Y, "Eigene Programme über Kunden");
        Assert.True(Mitte(nachher[5]).Y < Mitte(nachher[7]).Y, "Kunden über Cloud");
        Assert.True(Mitte(nachher[7]).Y < Mitte(nachher[10]).Y, "Cloud über Programme");
    }

    [Fact]
    public void Anordnen_LesezeichenBleibtLinksAussen()
    {
        var nachher = LayoutTransfer.Arrange(EchteAnordnung(), ZweiMonitore, NurLaptop);

        // Lesezeichen (0) lag als einziger Bereich ganz links — es muss der
        // linkeste bleiben, sonst sieht die Anordnung fremd aus.
        var lesezeichen = Mitte(nachher[0]).X;
        for (var i = 1; i < nachher.Count; i++)
            Assert.True(lesezeichen < Mitte(nachher[i]).X,
                $"Lesezeichen muss links von Bereich {i} bleiben.");
    }

    [Fact]
    public void PasstOhnehin_WirdNichtsVeraendert()
    {
        // Praesentationsfall: Beamer kommt dazu, die Flaeche wird nur groesser.
        var lage = new List<LayoutRect>
        {
            new() { X = 100, Y = 100, Width = 300, Height = 200 },
            new() { X = 500, Y = 100, Width = 300, Height = 200 },
            new() { X = 100, Y = 400, Width = 300, Height = 200 },
        };

        Assert.True(LayoutTransfer.FitsWithoutChange(lage, NurLaptop));
        // Und erst recht auf der groesseren Flaeche.
        Assert.True(LayoutTransfer.FitsWithoutChange(lage, ZweiMonitore));
    }

    [Fact]
    public void PasstNicht_WennEinBereichHerausragt()
    {
        var lage = new List<LayoutRect>
        {
            new() { X = 100, Y = 100, Width = 300, Height = 200 },
            new() { X = 3000, Y = 100, Width = 300, Height = 200 }, // zweiter Monitor
        };

        Assert.False(LayoutTransfer.FitsWithoutChange(lage, NurLaptop));
    }

    [Fact]
    public void PasstNicht_WennSichZweiUeberschneiden()
    {
        var lage = new List<LayoutRect>
        {
            new() { X = 100, Y = 100, Width = 300, Height = 200 },
            new() { X = 200, Y = 150, Width = 300, Height = 200 },
        };

        Assert.False(LayoutTransfer.FitsWithoutChange(lage, NurLaptop));
    }

    [Fact]
    public void Anordnen_IstStabil_ZweiterDurchlaufAendertNichts()
    {
        // Wichtig fuer haeufiges Umstecken: dieselbe Ausgangslage muss immer
        // dieselbe Anordnung ergeben, sonst wandern die Bereiche bei jedem
        // Anstecken ein Stueck weiter.
        var erste = LayoutTransfer.Arrange(EchteAnordnung(), ZweiMonitore, NurLaptop);
        var zweite = LayoutTransfer.Arrange(EchteAnordnung(), ZweiMonitore, NurLaptop);

        for (var i = 0; i < erste.Count; i++)
        {
            Assert.Equal(erste[i].X, zweite[i].X);
            Assert.Equal(erste[i].Y, zweite[i].Y);
        }
    }

    [Fact]
    public void Anordnen_ErgebnisPasstDannOhneWeitereAenderung()
    {
        // Nach dem Anordnen muss FitsWithoutChange true liefern — sonst wuerde
        // beim naechsten Wechsel erneut umsortiert.
        var ergebnis = LayoutTransfer.Arrange(EchteAnordnung(), ZweiMonitore, NurLaptop);
        Assert.True(LayoutTransfer.FitsWithoutChange(ergebnis, NurLaptop));
    }

    [Fact]
    public void Enclose_ErweitertUmAussenliegendeBereiche()
    {
        var bekannt = new LayoutTransfer.Area(0, 0, 1707, 1040);
        var items = new List<LayoutRect> { new() { X = 3000, Y = 100, Width = 400, Height = 300 } };

        var flaeche = LayoutTransfer.Enclose(bekannt, items);

        Assert.NotNull(flaeche);
        Assert.True(flaeche!.Value.X + flaeche.Value.Width >= 3400,
            "Die Fläche muss den außenliegenden Bereich umfassen.");
    }

    [Fact]
    public void Enclose_OhneBekannteFlaeche_NimmtDieBereiche()
    {
        var items = new List<LayoutRect>
        {
            new() { X = 100, Y = 50, Width = 200, Height = 100 },
            new() { X = 500, Y = 250, Width = 200, Height = 100 },
        };

        var flaeche = LayoutTransfer.Enclose(null, items);

        Assert.NotNull(flaeche);
        Assert.Equal(100, flaeche!.Value.X);
        Assert.Equal(50, flaeche.Value.Y);
        Assert.Equal(600, flaeche.Value.Width);   // 700 - 100
        Assert.Equal(300, flaeche.Value.Height);  // 350 - 50
    }

    private static (double X, double Y) Mitte(LayoutRect r)
        => (r.X + r.Width / 2, r.Y + r.Height / 2);
}
