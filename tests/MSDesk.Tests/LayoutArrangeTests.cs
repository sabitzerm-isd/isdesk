using MSDesk.Models;
using MSDesk.Services;

namespace MSDesk.Tests;

/// Gemeinsame Neuanordnung beim Wechsel auf eine unbekannte Bildschirm-Konfiguration.
/// Kernanforderung: danach darf sich KEIN Bereich mit einem anderen ueberschneiden.
public class LayoutArrangeTests
{
    private static readonly LayoutTransfer.Area ZweiMonitore = new(0, 0, 6000, 1440);
    private static readonly LayoutTransfer.Area NurLaptop = new(0, 0, 1920, 1040);

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

    /// Nachbildung der gemeldeten Lage: Bereiche auf ZWEI Monitoren, teils an
    /// derselben relativen Stelle — genau die landeten vorher uebereinander.
    private static List<LayoutRect> ElfBereicheAufZweiMonitoren() => new()
    {
        new LayoutRect { X = 20,   Y = 20,   Width = 430, Height = 430 }, // Lesezeichen
        new LayoutRect { X = 3450, Y = 250,  Width = 300, Height = 130 }, // ISD
        new LayoutRect { X = 3760, Y = 250,  Width = 290, Height = 130 }, // Support
        new LayoutRect { X = 3450, Y = 410,  Width = 300, Height = 130 }, // Eigene Programme
        new LayoutRect { X = 3760, Y = 410,  Width = 290, Height = 130 }, // KI
        new LayoutRect { X = 3590, Y = 570,  Width = 300, Height = 130 }, // Kunden
        new LayoutRect { X = 3900, Y = 570,  Width = 290, Height = 130 }, // Papierkorb
        new LayoutRect { X = 3590, Y = 725,  Width = 300, Height = 130 }, // Cloud
        new LayoutRect { X = 3320, Y = 880,  Width = 300, Height = 190 }, // Ablage
        new LayoutRect { X = 3480, Y = 880,  Width = 560, Height = 190 }, // Programme
        new LayoutRect { X = 5200, Y = 120,  Width = 300, Height = 130 }, // weiterer Monitor
    };

    [Fact]
    public void Arrange_KeineUeberschneidungen()
    {
        var ergebnis = LayoutTransfer.Arrange(ElfBereicheAufZweiMonitoren(), ZweiMonitore, NurLaptop);
        KeineUeberschneidungen(ergebnis);
    }

    [Fact]
    public void Arrange_AllesInnerhalbDerArbeitsflaeche()
    {
        var ergebnis = LayoutTransfer.Arrange(ElfBereicheAufZweiMonitoren(), ZweiMonitore, NurLaptop);

        foreach (var r in ergebnis)
        {
            Assert.True(r.X >= NurLaptop.X, $"links heraus: {r.X}");
            Assert.True(r.Y >= NurLaptop.Y, $"oben heraus: {r.Y}");
            Assert.True(r.X + r.Width <= NurLaptop.X + NurLaptop.Width + 0.5, $"rechts heraus: {r.X + r.Width}");
            Assert.True(r.Y + r.Height <= NurLaptop.Y + NurLaptop.Height + 0.5, $"unten heraus: {r.Y + r.Height}");
        }
    }

    [Fact]
    public void Arrange_BehaeltDieReihenfolgeDerRueckgabe()
    {
        var eingabe = ElfBereicheAufZweiMonitoren();
        var ergebnis = LayoutTransfer.Arrange(eingabe, ZweiMonitore, NurLaptop);

        // Gleiche Anzahl, gleiche Position in der Liste (fuer die Zuordnung
        // zum jeweiligen Bereich unverzichtbar).
        Assert.Equal(eingabe.Count, ergebnis.Count);
    }

    [Fact]
    public void Arrange_ErsterBereichBleibtLinksOben()
    {
        // Der Bereich, der vorher links oben lag, muss auch danach der linkeste
        // und oberste sein. Er wird bewusst NICHT in die Ecke gezwungen — die
        // Anordnung soll der gewohnten aehneln, nicht neu aufgereiht werden.
        var ergebnis = LayoutTransfer.Arrange(ElfBereicheAufZweiMonitoren(), ZweiMonitore, NurLaptop);

        var lesezeichen = ergebnis[0];
        for (var i = 1; i < ergebnis.Count; i++)
            Assert.True(lesezeichen.X < ergebnis[i].X + ergebnis[i].Width,
                $"Lesezeichen muss links von Bereich {i} beginnen.");
    }

    [Fact]
    public void Arrange_VieleBereiche_PasstDurchVerkleinern()
    {
        // 20 grosse Bereiche auf eine kleine Flaeche: muss trotzdem passen.
        var viele = Enumerable.Range(0, 20)
            .Select(i => new LayoutRect { X = i * 300, Y = (i % 4) * 300, Width = 400, Height = 300 })
            .ToList();

        var ergebnis = LayoutTransfer.Arrange(viele, ZweiMonitore, NurLaptop);

        KeineUeberschneidungen(ergebnis);
        foreach (var r in ergebnis)
        {
            Assert.True(r.X + r.Width <= NurLaptop.X + NurLaptop.Width + 0.5);
            Assert.True(r.Y + r.Height <= NurLaptop.Y + NurLaptop.Height + 0.5);
        }
    }

    [Fact]
    public void Arrange_EinzelnerBereich_BleibtGross()
    {
        var einer = new List<LayoutRect> { new() { X = 100, Y = 100, Width = 600, Height = 400 } };
        var ergebnis = LayoutTransfer.Arrange(einer, ZweiMonitore, NurLaptop);

        Assert.Single(ergebnis);
        Assert.True(ergebnis[0].Width > LayoutTransfer.MinWidth);
    }

    [Fact]
    public void Arrange_LeereListe_BleibtLeer()
    {
        Assert.Empty(LayoutTransfer.Arrange(new List<LayoutRect>(), ZweiMonitore, NurLaptop));
    }
}
