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
    // Danach nur noch der Laptop (2560 x 1600, Arbeitsflaeche 1520 hoch).
    private static readonly LayoutTransfer.Area NurLaptop = new(0, 0, 2560, 1520);

    /// Quellflaeche wie im Programm: die tatsaechliche Ausdehnung der Bereiche.
    private static LayoutTransfer.Area Ausdehnung(IReadOnlyList<LayoutRect> items)
        => LayoutTransfer.Enclose(null, items)!.Value;

    /// <summary>
    /// Die TATSAECHLICHE Anordnung aus der Konfiguration des Anwenders
    /// (Doppelmonitor). Bewusst die echten Zahlen: nur so pruefen die Tests den
    /// Fall, der wirklich auftritt.
    /// </summary>
    private static List<LayoutRect> EchteAnordnung() => new()
    {
        /* 0 Lesezeichen */ new LayoutRect { X =   40, Y =  710, Width = 700, Height = 644 },
        /* 1 ISD         */ new LayoutRect { X = 2820, Y =  340, Width = 572, Height = 175 },
        /* 2 Support     */ new LayoutRect { X = 2400, Y =  340, Width = 400, Height = 180 },
        /* 3 EigeneProg  */ new LayoutRect { X = 2990, Y =  540, Width = 400, Height = 172 },
        /* 4 Papierkorb  */ new LayoutRect { X = 2800, Y =  540, Width = 180, Height = 175 },
        /* 5 Kunden      */ new LayoutRect { X = 2990, Y =  730, Width = 400, Height = 189 },
        /* 6 Cloud       */ new LayoutRect { X = 2990, Y =  940, Width = 400, Height = 183 },
        /* 7 KI          */ new LayoutRect { X = 2650, Y =  940, Width = 330, Height = 180 },
        /* 8 Ablage      */ new LayoutRect { X = 1700, Y = 1130, Width = 929, Height = 244 },
        /* 9 Launcher    */ new LayoutRect { X = 2650, Y = 1130, Width = 180, Height = 244 },
        /*10 Programme   */ new LayoutRect { X = 2840, Y = 1130, Width = 554, Height = 238 },
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
        var ergebnis = LayoutTransfer.Arrange(EchteAnordnung(), Ausdehnung(EchteAnordnung()), NurLaptop);
        KeineUeberschneidungen(ergebnis);
    }

    [Fact]
    public void Anordnen_AllesInnerhalbDesBildschirms()
    {
        var ergebnis = LayoutTransfer.Arrange(EchteAnordnung(), Ausdehnung(EchteAnordnung()), NurLaptop);

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
        var nachher = LayoutTransfer.Arrange(vorher, Ausdehnung(vorher), NurLaptop);

        // Support (2) lag links von ISD (1) — das muss so bleiben.
        Assert.True(Mitte(nachher[2]).X < Mitte(nachher[1]).X,
            "Support muss links von ISD bleiben.");
        // Papierkorb (4) links von Eigene Programme (3)
        Assert.True(Mitte(nachher[4]).X < Mitte(nachher[3]).X,
            "Papierkorb muss links von Eigene Programme bleiben.");
        // Ablage (8) links von Launcher (9) links von Programme (10)
        Assert.True(Mitte(nachher[8]).X < Mitte(nachher[9]).X,
            "Ablage muss links von Launcher bleiben.");
        Assert.True(Mitte(nachher[9]).X < Mitte(nachher[10]).X,
            "Launcher muss links von Programme bleiben.");
    }

    [Fact]
    public void Anordnen_BehaeltDieObenUntenBeziehung()
    {
        var nachher = LayoutTransfer.Arrange(EchteAnordnung(), Ausdehnung(EchteAnordnung()), NurLaptop);

        // Die rechte Spalte war von oben nach unten:
        // ISD (1), Eigene Programme (3), Kunden (5), Cloud (6), Programme (10)
        Assert.True(Mitte(nachher[1]).Y < Mitte(nachher[3]).Y, "ISD über Eigene Programme");
        Assert.True(Mitte(nachher[3]).Y < Mitte(nachher[5]).Y, "Eigene Programme über Kunden");
        Assert.True(Mitte(nachher[5]).Y < Mitte(nachher[6]).Y, "Kunden über Cloud");
        Assert.True(Mitte(nachher[6]).Y < Mitte(nachher[10]).Y, "Cloud über Programme");
    }

    [Fact]
    public void Anordnen_LesezeichenBleibtLinksAussen()
    {
        var nachher = LayoutTransfer.Arrange(EchteAnordnung(), Ausdehnung(EchteAnordnung()), NurLaptop);

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
        // Und erst recht auf der groesseren Flaeche (Beamer dazu).
        Assert.True(LayoutTransfer.FitsWithoutChange(lage, new LayoutTransfer.Area(0, 0, 6000, 1440)));
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
        var erste = LayoutTransfer.Arrange(EchteAnordnung(), Ausdehnung(EchteAnordnung()), NurLaptop);
        var zweite = LayoutTransfer.Arrange(EchteAnordnung(), Ausdehnung(EchteAnordnung()), NurLaptop);

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
        var ergebnis = LayoutTransfer.Arrange(EchteAnordnung(), Ausdehnung(EchteAnordnung()), NurLaptop);
        Assert.True(LayoutTransfer.FitsWithoutChange(ergebnis, NurLaptop));
    }

    [Fact]
    public void Groessen_BleibenImmerUnveraendert()
    {
        // Kernanforderung: Beim automatischen Anordnen darf sich die Groesse
        // NIE aendern — sonst werden Beschriftungen und Symbole abgeschnitten.
        var vorher = EchteAnordnung();
        var nachher = LayoutTransfer.Arrange(vorher, Ausdehnung(vorher), NurLaptop);

        for (var i = 0; i < vorher.Count; i++)
        {
            Assert.Equal(vorher[i].Width, nachher[i].Width);
            Assert.Equal(vorher[i].Height, nachher[i].Height);
        }
    }

    [Fact]
    public void Groessen_BleibenAuchBeiEngemPlatzUnveraendert()
    {
        // Selbst wenn die vertraute Anordnung nicht mehr passt und dicht
        // gepackt werden muss: die Groessen bleiben.
        var eng = new LayoutTransfer.Area(0, 0, 1280, 800);
        var vorher = EchteAnordnung();
        var nachher = LayoutTransfer.Arrange(vorher, Ausdehnung(vorher), eng);

        for (var i = 0; i < vorher.Count; i++)
        {
            Assert.Equal(Math.Min(vorher[i].Width, eng.Width), nachher[i].Width);
            Assert.Equal(Math.Min(vorher[i].Height, eng.Height), nachher[i].Height);
        }
        KeineUeberschneidungen(nachher);
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
