using MSDesk.Models;
using MSDesk.Services;
using static MSDesk.Services.DisplaySwitchPlan;

namespace MSDesk.Tests;

/// <summary>
/// Spielt den vollstaendigen Ablauf durch:
///   anstecken → einrichten → abstecken → verschieben → WIEDER anstecken.
///
/// Kernanforderung: Eine von Hand eingerichtete und gespeicherte Anordnung muss
/// beim Wiederanstecken EXAKT so zurueckkommen — Position und Groesse.
/// </summary>
public class DisplaySwitchPlanTests
{
    // Zwei Bildschirme: Ultrawide links, zweiter Monitor rechts daneben.
    private const string KeyZwei = "3440x1440@0,0|2560x1600@3440,172";
    private const string KeyLaptop = "2560x1600@0,0";

    private static readonly LayoutTransfer.Area FlaecheZwei = new(0, 0, 6000, 1772);
    private static readonly LayoutTransfer.Area WorkZwei = new(0, 0, 3440, 1360);

    private static readonly LayoutTransfer.Area FlaecheLaptop = new(0, 0, 2560, 1600);
    private static readonly LayoutTransfer.Area WorkLaptop = new(0, 0, 2560, 1520);

    /// Anordnung am Doppelmonitor, wie von Hand eingerichtet.
    private static List<LayoutRect> AnordnungZwei() => new()
    {
        new LayoutRect { X = 40,   Y = 710,  Width = 700, Height = 644 }, // Lesezeichen
        new LayoutRect { X = 3620, Y = 250,  Width = 460, Height = 145 }, // ISD
        new LayoutRect { X = 4110, Y = 250,  Width = 380, Height = 145 }, // Support
        new LayoutRect { X = 3620, Y = 420,  Width = 460, Height = 150 }, // Eigene Programme
        new LayoutRect { X = 4110, Y = 420,  Width = 380, Height = 150 }, // KI
        new LayoutRect { X = 3620, Y = 600,  Width = 460, Height = 150 }, // Kunden
        new LayoutRect { X = 4110, Y = 600,  Width = 200, Height = 150 }, // Papierkorb
        new LayoutRect { X = 3620, Y = 780,  Width = 460, Height = 150 }, // Cloud
        new LayoutRect { X = 3100, Y = 960,  Width = 560, Height = 190 }, // Ablage
        new LayoutRect { X = 3700, Y = 960,  Width = 200, Height = 190 }, // Launcher
        new LayoutRect { X = 3940, Y = 960,  Width = 560, Height = 190 }, // Programme
    };

    /// Baut die Bereichsliste: aktuelle Lage + bereits gemerkte Anordnungen.
    private static List<Fence> Bereiche(
        IReadOnlyList<LayoutRect> aktuell,
        params (string Key, IReadOnlyList<LayoutRect> Lage)[] gemerkt)
    {
        var list = new List<Fence>();
        for (var i = 0; i < aktuell.Count; i++)
        {
            var layouts = new Dictionary<string, LayoutRect>();
            foreach (var (key, lage) in gemerkt) layouts[key] = Copy(lage[i]);
            list.Add(new Fence(Copy(aktuell[i]), layouts));
        }
        return list;
    }

    private static LayoutRect Copy(LayoutRect r)
        => new() { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height };

    private static void Gleich(IReadOnlyList<LayoutRect> erwartet, IReadOnlyList<LayoutRect> ist)
    {
        Assert.Equal(erwartet.Count, ist.Count);
        for (var i = 0; i < erwartet.Count; i++)
        {
            Assert.Equal(erwartet[i].X, ist[i].X);
            Assert.Equal(erwartet[i].Y, ist[i].Y);
            Assert.Equal(erwartet[i].Width, ist[i].Width);
            Assert.Equal(erwartet[i].Height, ist[i].Height);
        }
    }

    private static bool Ueberschneidet(LayoutRect a, LayoutRect b)
        => a.X < b.X + b.Width && b.X < a.X + a.Width
        && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    // ===================== Der vollstaendige Ablauf =====================

    [Fact]
    public void Ablauf_AbsteckenUndWiederAnstecken_StelltGenauWiederHer()
    {
        var amDoppel = AnordnungZwei();

        // 1. ABSTECKEN: Laptop-Konfiguration ist unbekannt → wird abgeleitet.
        var beimAbstecken = Compute(
            Bereiche(amDoppel, (KeyZwei, amDoppel)), KeyLaptop, FlaecheLaptop, WorkLaptop);

        Assert.Equal(PlanKind.Derived, beimAbstecken.Kind);
        var amLaptop = beimAbstecken.Positions;

        // 2. WIEDER ANSTECKEN: Die Doppelmonitor-Anordnung ist vollstaendig
        //    gemerkt — sie muss EXAKT zurueckkommen.
        var beimAnstecken = Compute(
            Bereiche(amLaptop, (KeyZwei, amDoppel), (KeyLaptop, amLaptop)),
            KeyZwei, FlaecheZwei, WorkZwei);

        Assert.Equal(PlanKind.Restored, beimAnstecken.Kind);
        Gleich(amDoppel, beimAnstecken.Positions);
    }

    [Fact]
    public void Ablauf_MitManuellemVerschieben_BleibtErhalten()
    {
        var amDoppel = AnordnungZwei();

        // Abstecken → ableiten
        var amLaptop = Compute(
            Bereiche(amDoppel, (KeyZwei, amDoppel)), KeyLaptop, FlaecheLaptop, WorkLaptop).Positions;

        // Am Laptop wird von Hand zurechtgeschoben und gespeichert.
        var handArrangiert = amLaptop.Select(r => new LayoutRect
        {
            X = Math.Max(0, r.X - 30), Y = Math.Max(0, r.Y - 20), Width = r.Width, Height = r.Height
        }).ToList();

        // Anstecken → Doppelmonitor exakt zurueck
        var zurueck = Compute(
            Bereiche(handArrangiert, (KeyZwei, amDoppel), (KeyLaptop, handArrangiert)),
            KeyZwei, FlaecheZwei, WorkZwei);
        Gleich(amDoppel, zurueck.Positions);

        // Erneut abstecken → die HAND-Anordnung muss zurueckkommen, nicht neu abgeleitet
        var wiederLaptop = Compute(
            Bereiche(amDoppel, (KeyZwei, amDoppel), (KeyLaptop, handArrangiert)),
            KeyLaptop, FlaecheLaptop, WorkLaptop);

        Assert.Equal(PlanKind.Restored, wiederLaptop.Kind);
        Gleich(handArrangiert, wiederLaptop.Positions);
    }

    [Fact]
    public void MehrfachesUmstecken_AendertNichtsMehr()
    {
        // Haeufiges Umstecken (Praesentationen) darf die Anordnung nicht
        // Stueck fuer Stueck verschieben.
        var amDoppel = AnordnungZwei();
        var amLaptop = Compute(
            Bereiche(amDoppel, (KeyZwei, amDoppel)), KeyLaptop, FlaecheLaptop, WorkLaptop).Positions;

        var aktuell = amLaptop;
        for (var runde = 0; runde < 5; runde++)
        {
            var an = Compute(Bereiche(aktuell, (KeyZwei, amDoppel), (KeyLaptop, amLaptop)),
                             KeyZwei, FlaecheZwei, WorkZwei);
            Gleich(amDoppel, an.Positions);

            var ab = Compute(Bereiche(an.Positions, (KeyZwei, amDoppel), (KeyLaptop, amLaptop)),
                             KeyLaptop, FlaecheLaptop, WorkLaptop);
            Gleich(amLaptop, ab.Positions);

            aktuell = ab.Positions;
        }
    }

    // ===================== Einzelne Anforderungen =====================

    [Fact]
    public void Abgeleitet_KeineUeberschneidungen()
    {
        var amDoppel = AnordnungZwei();
        var plan = Compute(Bereiche(amDoppel, (KeyZwei, amDoppel)), KeyLaptop, FlaecheLaptop, WorkLaptop);

        for (var i = 0; i < plan.Positions.Count; i++)
        for (var j = i + 1; j < plan.Positions.Count; j++)
            Assert.False(Ueberschneidet(plan.Positions[i], plan.Positions[j]),
                $"Bereich {i} und {j} überschneiden sich.");
    }

    [Fact]
    public void Abgeleitet_GroessenBleibenUnveraendert()
    {
        var amDoppel = AnordnungZwei();
        var plan = Compute(Bereiche(amDoppel, (KeyZwei, amDoppel)), KeyLaptop, FlaecheLaptop, WorkLaptop);

        for (var i = 0; i < amDoppel.Count; i++)
        {
            Assert.Equal(amDoppel[i].Width, plan.Positions[i].Width);
            Assert.Equal(amDoppel[i].Height, plan.Positions[i].Height);
        }
    }

    [Fact]
    public void Abgeleitet_AllesAufDemBildschirm()
    {
        var amDoppel = AnordnungZwei();
        var plan = Compute(Bereiche(amDoppel, (KeyZwei, amDoppel)), KeyLaptop, FlaecheLaptop, WorkLaptop);

        foreach (var r in plan.Positions)
        {
            Assert.True(r.X >= -0.5, $"links heraus: {r.X}");
            Assert.True(r.Y >= -0.5, $"oben heraus: {r.Y}");
            Assert.True(r.X + r.Width <= WorkLaptop.Width + 0.5, $"rechts heraus: {r.X + r.Width}");
            Assert.True(r.Y + r.Height <= WorkLaptop.Height + 0.5, $"unten heraus: {r.Y + r.Height}");
        }
    }

    [Fact]
    public void BeamerKommtDazu_NichtsWirdVerschoben()
    {
        // Praesentation: die Flaeche wird nur groesser — es darf sich nichts ruehren.
        var lage = new List<LayoutRect>
        {
            new() { X = 100, Y = 100, Width = 400, Height = 300 },
            new() { X = 600, Y = 100, Width = 400, Height = 300 },
        };

        var mitBeamer = new LayoutTransfer.Area(0, 0, 4480, 1600);
        var plan = Compute(Bereiche(lage), "beamer", mitBeamer, WorkLaptop);

        Assert.Equal(PlanKind.Unchanged, plan.Kind);
        Gleich(lage, plan.Positions);
    }

    [Fact]
    public void NeuerBereichDazu_UebrigeBleibenWoSieSind()
    {
        // Ein Bereich ohne gemerkte Anordnung darf die anderen nicht umwerfen,
        // solange alles ueberschneidungsfrei bleibt.
        var lage = new List<LayoutRect>
        {
            new() { X = 100, Y = 100, Width = 400, Height = 300 },
            new() { X = 600, Y = 100, Width = 400, Height = 300 },
            new() { X = 1100, Y = 100, Width = 400, Height = 300 }, // neu
        };

        var fences = new List<Fence>
        {
            new(Copy(lage[0]), new Dictionary<string, LayoutRect> { [KeyLaptop] = Copy(lage[0]) }),
            new(Copy(lage[1]), new Dictionary<string, LayoutRect> { [KeyLaptop] = Copy(lage[1]) }),
            new(Copy(lage[2]), new Dictionary<string, LayoutRect>()), // ohne Anordnung
        };

        var plan = Compute(fences, KeyLaptop, FlaecheLaptop, WorkLaptop);

        Assert.Equal(PlanKind.Unchanged, plan.Kind);
        Gleich(lage, plan.Positions);
    }

    [Fact]
    public void OhneBereiche_KeinFehler()
    {
        var plan = Compute(Array.Empty<Fence>(), KeyLaptop, FlaecheLaptop, WorkLaptop);
        Assert.Empty(plan.Positions);
    }
}
