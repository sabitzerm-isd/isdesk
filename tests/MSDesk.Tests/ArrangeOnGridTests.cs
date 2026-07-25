using MSDesk.Models;
using MSDesk.Services;

namespace MSDesk.Tests;

/// <summary>
/// Anordnen an einem gedachten Raster: ueberall gleicher Zwischenraum,
/// Groessen bleiben unveraendert.
/// </summary>
public class ArrangeOnGridTests
{
    private static readonly LayoutTransfer.Area Flaeche = new(0, 0, 1920, 1040);
    private const double Gap = 16;

    private static List<LayoutRect> Bereiche() => new()
    {
        new LayoutRect { X = 100, Y = 100, Width = 400, Height = 200 },
        new LayoutRect { X = 600, Y = 110, Width = 300, Height = 250 },
        new LayoutRect { X = 950, Y = 105, Width = 350, Height = 180 },
        new LayoutRect { X = 120, Y = 500, Width = 500, Height = 220 },
        new LayoutRect { X = 700, Y = 520, Width = 280, Height = 200 },
    };

    [Fact]
    public void Groessen_BleibenUnveraendert()
    {
        var vorher = Bereiche();
        var nachher = LayoutTransfer.ArrangeOnGrid(vorher, Flaeche, Gap);

        for (var i = 0; i < vorher.Count; i++)
        {
            Assert.Equal(vorher[i].Width, nachher[i].Width);
            Assert.Equal(vorher[i].Height, nachher[i].Height);
        }
    }

    [Fact]
    public void Zwischenraum_IstUeberallGleich()
    {
        var nachher = LayoutTransfer.ArrangeOnGrid(Bereiche(), Flaeche, Gap);

        // Innerhalb einer Zeile: Abstand zwischen rechtem und naechstem linken Rand.
        var zeilen = nachher.GroupBy(r => r.Y).OrderBy(g => g.Key);
        foreach (var zeile in zeilen)
        {
            var sortiert = zeile.OrderBy(r => r.X).ToList();
            for (var i = 1; i < sortiert.Count; i++)
            {
                var abstand = sortiert[i].X - (sortiert[i - 1].X + sortiert[i - 1].Width);
                Assert.True(Math.Abs(abstand - Gap) < 1.5,
                    $"Zwischenraum {abstand} statt {Gap}");
            }
        }
    }

    [Fact]
    public void KeineUeberschneidungen()
    {
        var nachher = LayoutTransfer.ArrangeOnGrid(Bereiche(), Flaeche, Gap);

        for (var i = 0; i < nachher.Count; i++)
        for (var j = i + 1; j < nachher.Count; j++)
        {
            var a = nachher[i];
            var b = nachher[j];
            var ueberschneidet = a.X < b.X + b.Width && b.X < a.X + a.Width
                              && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
            Assert.False(ueberschneidet, $"Bereich {i} und {j} überschneiden sich.");
        }
    }

    [Fact]
    public void ReihenfolgeBleibtErhalten()
    {
        var nachher = LayoutTransfer.ArrangeOnGrid(Bereiche(), Flaeche, Gap);

        // Aufgebaut wird von unten rechts. Die Zeilen werden dicht gefuellt, ein
        // Bereich kann also die Zeile wechseln — die REIHENFOLGE muss aber
        // dieselbe bleiben: 0 vor 1 vor 2 vor 3 vor 4 (von oben links gelesen).
        var reihenfolge = Enumerable.Range(0, nachher.Count)
            .OrderBy(i => nachher[i].Y)
            .ThenBy(i => nachher[i].X)
            .ToList();

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, reihenfolge);
    }

    [Fact]
    public void BeginntUntenRechts()
    {
        var nachher = LayoutTransfer.ArrangeOnGrid(Bereiche(), Flaeche, Gap);

        // Der zuletzt gelesene Bereich (4) sitzt rechts aussen — dort beginnt
        // der Aufbau. Innerhalb einer Zeile stehen alle auf gleicher Hoehe,
        // deshalb beruehrt der HOECHSTE Bereich der untersten Zeile den Rand.
        Assert.Equal(Flaeche.Width - Gap, nachher[4].X + nachher[4].Width);
        Assert.Equal(Flaeche.Height - Gap, nachher.Max(r => r.Y + r.Height));
    }

    [Fact]
    public void LinkeBildschirmhaelfteBleibtMoeglichstFrei()
    {
        // Fuer die Desktop-Symbole: wenn der Platz reicht, darf nichts unnoetig
        // in die linke Haelfte ragen.
        var wenige = new List<LayoutRect>
        {
            new() { X = 100, Y = 100, Width = 300, Height = 200 },
            new() { X = 500, Y = 100, Width = 300, Height = 200 },
        };

        var nachher = LayoutTransfer.ArrangeOnGrid(wenige, Flaeche, Gap);

        foreach (var r in nachher)
            Assert.True(r.X > Flaeche.Width / 2,
                $"Bereich ragt unnötig in die linke Hälfte: X={r.X}");
    }

    [Fact]
    public void AllesInnerhalbDerFlaeche()
    {
        var nachher = LayoutTransfer.ArrangeOnGrid(Bereiche(), Flaeche, Gap);

        foreach (var r in nachher)
        {
            Assert.True(r.X >= 0, $"links heraus: {r.X}");
            Assert.True(r.X + r.Width <= Flaeche.Width + 0.5, $"rechts heraus: {r.X + r.Width}");
            Assert.True(r.Y >= 0, $"oben heraus: {r.Y}");
        }
    }

    [Fact]
    public void HaeltDenAbstandZumRechtenRand()
    {
        var nachher = LayoutTransfer.ArrangeOnGrid(Bereiche(), Flaeche, Gap);
        var rechtester = nachher.OrderByDescending(r => r.X + r.Width).First();

        Assert.Equal(Flaeche.Width - Gap, rechtester.X + rechtester.Width);
    }

    [Fact]
    public void LeereListe_BleibtLeer()
    {
        Assert.Empty(LayoutTransfer.ArrangeOnGrid(new List<LayoutRect>(), Flaeche, Gap));
    }

    [Fact]
    public void IstWiederholbar_ZweiterAufrufAendertNichts()
    {
        var erste = LayoutTransfer.ArrangeOnGrid(Bereiche(), Flaeche, Gap);
        var zweite = LayoutTransfer.ArrangeOnGrid(erste, Flaeche, Gap);

        for (var i = 0; i < erste.Count; i++)
        {
            Assert.Equal(erste[i].X, zweite[i].X);
            Assert.Equal(erste[i].Y, zweite[i].Y);
        }
    }
}
