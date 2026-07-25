using MSDesk.Models;

namespace MSDesk.Services;

/// <summary>
/// Uebertraegt die Anordnung der Bereiche auf eine bislang unbekannte
/// Bildschirm-Konfiguration. Wird genau einmal gebraucht: beim ersten Mal, wenn
/// z. B. der zweite Monitor abgesteckt wird — danach ist fuer diese Konfiguration
/// ein eigenes Layout gespeichert und wird unveraendert weiterverwendet.
///
/// Grundgedanke: Die Bereiche bleiben dieselben, also soll auch ihre relative
/// Anordnung erhalten bleiben. Die Position wird anteilig umgerechnet, die
/// Groesse mit einem einheitlichen Faktor — sonst wuerden Bereiche verzerrt,
/// wenn sich das Seitenverhaeltnis stark aendert (zwei Monitore → einer).
/// </summary>
public static class LayoutTransfer
{
    public const double MinWidth = 110;
    public const double MinHeight = 80;

    /// Ein Bereich in einem Koordinatensystem (DIP).
    public readonly record struct Area(double X, double Y, double Width, double Height);

    /// <summary>
    /// Rechnet <paramref name="item"/> aus der Flaeche <paramref name="from"/>
    /// in die Flaeche <paramref name="to"/> um.
    ///
    /// Massgeblich ist der MITTELPUNKT, nicht die linke obere Ecke: sonst
    /// wandern breite Bereiche am rechten Rand beim Verkleinern nach links weg,
    /// und die Anordnung sieht danach anders aus als vorher.
    /// </summary>
    public static LayoutRect Map(LayoutRect item, Area from, Area to)
    {
        if (from.Width <= 0 || from.Height <= 0 || to.Width <= 0 || to.Height <= 0)
            return item; // ohne brauchbare Flaechen nichts veraendern

        // Die Groesse bleibt zunaechst UNVERAENDERT — nur begrenzt auf das, was
        // auf die Zielflaeche passt. Generell zu verkleinern waere falsch: wird
        // die Flaeche nur schmaler, aber nicht niedriger, wuerden die Bereiche
        // unnoetig geschrumpft und Beschriftungen abgeschnitten. Reicht der Platz
        // wirklich nicht, verkleinert <see cref="Arrange"/> schrittweise alle
        // gemeinsam.
        var width = Math.Min(to.Width, Math.Max(MinWidth, item.Width));
        var height = Math.Min(to.Height, Math.Max(MinHeight, item.Height));

        // Relative Lage des Mittelpunkts beibehalten.
        var relX = (item.X + item.Width / 2 - from.X) / from.Width;
        var relY = (item.Y + item.Height / 2 - from.Y) / from.Height;

        var x = to.X + relX * to.Width - width / 2;
        var y = to.Y + relY * to.Height - height / 2;

        // Vollstaendig innerhalb der Zielflaeche halten.
        x = Clamp(x, to.X, to.X + to.Width - width);
        y = Clamp(y, to.Y, to.Y + to.Height - height);

        return new LayoutRect { X = Math.Round(x), Y = Math.Round(y), Width = Math.Round(width), Height = Math.Round(height) };
    }

    /// <summary>
    /// Liegen alle Bereiche bereits vollstaendig in der Flaeche und ueberschneiden
    /// sich nicht? Dann darf NICHTS veraendert werden.
    ///
    /// Wichtig fuer Praesentationen: Wird ein Beamer zusaetzlich angesteckt, wird
    /// die Flaeche nur groesser — die Anordnung muss dann unangetastet bleiben.
    /// Jedes unnoetige Verschieben waere hier ein Fehler.
    /// </summary>
    public static bool FitsWithoutChange(IReadOnlyList<LayoutRect> items, Area area)
    {
        if (items.Count == 0) return true;
        if (area.Width <= 0 || area.Height <= 0) return true;

        foreach (var item in items)
        {
            if (item.X < area.X - 0.5 || item.Y < area.Y - 0.5) return false;
            if (item.X + item.Width > area.X + area.Width + 0.5) return false;
            if (item.Y + item.Height > area.Y + area.Height + 0.5) return false;
        }

        for (var i = 0; i < items.Count; i++)
        for (var j = i + 1; j < items.Count; j++)
            if (Overlaps(items[i], items[j])) return false;

        return true;
    }

    private static bool Overlaps(LayoutRect a, LayoutRect b)
        => a.X < b.X + b.Width && b.X < a.X + a.Width
        && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    /// <summary>
    /// Kleinste Flaeche, die sowohl <paramref name="known"/> als auch alle
    /// <paramref name="items"/> enthaelt. Damit ist sichergestellt, dass beim
    /// Uebertragen jeder Bereich innerhalb der Quellflaeche liegt — sonst kaeme
    /// seine relative Lage ueber 100 % und er wuerde an den Rand geklemmt.
    /// null, wenn es weder eine bekannte Flaeche noch Bereiche gibt.
    /// </summary>
    public static Area? Enclose(Area? known, IReadOnlyList<LayoutRect> items)
    {
        double left, top, right, bottom;

        if (known is { Width: > 0, Height: > 0 } k)
        {
            left = k.X; top = k.Y;
            right = k.X + k.Width; bottom = k.Y + k.Height;
        }
        else if (items.Count > 0)
        {
            left = items.Min(i => i.X);
            top = items.Min(i => i.Y);
            right = items.Max(i => i.X + i.Width);
            bottom = items.Max(i => i.Y + i.Height);
        }
        else
        {
            return null;
        }

        foreach (var item in items)
        {
            left = Math.Min(left, item.X);
            top = Math.Min(top, item.Y);
            right = Math.Max(right, item.X + item.Width);
            bottom = Math.Max(bottom, item.Y + item.Height);
        }

        return right > left && bottom > top
            ? new Area(left, top, right - left, bottom - top)
            : null;
    }

    /// Mindestens sichtbarer Anteil eines Bereichs (DIP), damit er greifbar bleibt.
    public const double MinVisible = 60;

    /// <summary>
    /// Schiebt einen Bereich in die Flaeche hinein, statt ihn auf einen festen
    /// Punkt zu setzen. Wichtig: mehrere Bereiche auf denselben Punkt zu setzen
    /// ergaebe einen Stapel — genau das passierte frueher.
    /// Liegt der Bereich ausreichend sichtbar, bleibt er unveraendert.
    /// </summary>
    public static LayoutRect ClampIntoArea(LayoutRect item, Area area)
    {
        if (area.Width <= 0 || area.Height <= 0) return item;

        var right = area.X + area.Width;
        var bottom = area.Y + area.Height;

        var sichtbar = item.X + item.Width > area.X + MinVisible
                       && item.X < right - MinVisible
                       && item.Y + item.Height > area.Y + MinVisible
                       && item.Y < bottom - MinVisible;
        if (sichtbar) return item;

        return new LayoutRect
        {
            X = Clamp(item.X, area.X, Math.Max(area.X, right - item.Width)),
            Y = Clamp(item.Y, area.Y, Math.Max(area.Y, bottom - item.Height)),
            Width = item.Width,
            Height = item.Height
        };
    }

    /// Abstand zwischen zwei Bereichen bei der Neuanordnung (DIP).
    private const double Gap = 8;

    /// Hoehe eines „Zeilenfachs" fuer die Sortierung — Bereiche innerhalb
    /// dieses Bandes gelten als in derselben Zeile liegend.
    private const double RowBand = 120;

    /// <summary>
    /// Ordnet ALLE Bereiche gemeinsam auf der neuen Flaeche an — so aehnlich wie
    /// moeglich zur bisherigen Anordnung und ohne Ueberschneidungen.
    ///
    /// Vorgehen:
    ///   1. Jeden Bereich anteilig abbilden (Mittelpunkt-treu, einheitlich
    ///      skaliert) — damit bleibt die vertraute Anordnung erhalten.
    ///   2. Verbleibende Ueberschneidungen durch VERSCHIEBEN aufloesen, jeweils
    ///      in die Richtung des kuerzeren Weges. Die Nachbarschaften bleiben so
    ///      erhalten; frueher wurde stattdessen alles in Leserichtung neu
    ///      aufgereiht, wodurch die Anordnung voellig anders aussah.
    ///   3. Geht es nicht auf, alles gemeinsam etwas verkleinern und erneut
    ///      versuchen. Erst wenn selbst das scheitert, wird aufgereiht.
    ///
    /// Die Rueckgabe hat dieselbe Reihenfolge wie die Eingabe.
    /// </summary>
    public static IReadOnlyList<LayoutRect> Arrange(IReadOnlyList<LayoutRect> items, Area from, Area to)
    {
        if (items.Count == 0) return items;
        if (to.Width <= 0 || to.Height <= 0) return items;

        // Die GROESSEN bleiben in jedem Fall unveraendert. Frueher wurde bei
        // Platzmangel schrittweise verkleinert — dabei wurden Beschriftungen und
        // Symbole abgeschnitten, was niemand will. Reicht der Platz nicht fuer
        // die vertraute Anordnung, wird stattdessen dicht gepackt.
        var mapped = items.Select(i => Map(i, from, to)).ToList();
        if (Relax(mapped, to)) return Round(mapped);

        // Rueckfall: am Raster aufreihen (von unten rechts), ebenfalls ohne
        // Groessenaenderung — garantiert ueberschneidungsfrei.
        return ArrangeOnGrid(items, to, Gap);
    }

    /// <summary>
    /// Schiebt ueberlappende Bereiche auseinander, bis sich keine mehr beruehren.
    /// true = geschafft (und alles liegt innerhalb der Flaeche).
    /// </summary>
    private static bool Relax(List<LayoutRect> rects, Area to)
    {
        const int MaxIterations = 260;

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var moved = false;

            for (var i = 0; i < rects.Count; i++)
            for (var j = i + 1; j < rects.Count; j++)
            {
                var a = rects[i];
                var b = rects[j];

                // Mit Abstand pruefen, damit zwischen den Bereichen eine Luecke bleibt.
                var overlapX = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X) + Gap;
                var overlapY = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y) + Gap;
                if (overlapX <= 0 || overlapY <= 0) continue;

                moved = true;

                var aCx = a.X + a.Width / 2;
                var bCx = b.X + b.Width / 2;
                var aCy = a.Y + a.Height / 2;
                var bCy = b.Y + b.Height / 2;

                if (overlapX <= overlapY)
                {
                    // Waagerecht trennen — der kuerzere Weg.
                    // Bei exakt gleicher Lage entscheidet der Index, sonst
                    // bewegte sich nichts und die Schleife liefe leer.
                    var aLinks = aCx < bCx || (Math.Abs(aCx - bCx) < 0.01 && i < j);
                    var (nachLinks, nachRechts) = aLinks ? (i, j) : (j, i);

                    // Wer am Rand steht, kann nicht ausweichen — dann geht der
                    // andere den GANZEN Weg. Ohne das blockieren sich Bereiche
                    // am Bildschirmrand gegenseitig und es konvergiert nie.
                    var platzLinks = rects[nachLinks].X - to.X;
                    var platzRechts = (to.X + to.Width) - (rects[nachRechts].X + rects[nachRechts].Width);

                    var (schubLinks, schubRechts) = Verteilen(overlapX, platzLinks, platzRechts);
                    rects[nachLinks] = Offset(rects[nachLinks], -schubLinks, 0);
                    rects[nachRechts] = Offset(rects[nachRechts], schubRechts, 0);
                }
                else
                {
                    var aOben = aCy < bCy || (Math.Abs(aCy - bCy) < 0.01 && i < j);
                    var (nachOben, nachUnten) = aOben ? (i, j) : (j, i);

                    var platzOben = rects[nachOben].Y - to.Y;
                    var platzUnten = (to.Y + to.Height) - (rects[nachUnten].Y + rects[nachUnten].Height);

                    var (schubOben, schubUnten) = Verteilen(overlapY, platzOben, platzUnten);
                    rects[nachOben] = Offset(rects[nachOben], 0, -schubOben);
                    rects[nachUnten] = Offset(rects[nachUnten], 0, schubUnten);
                }
            }

            // Nach jedem Durchgang zurueck in die Flaeche holen.
            for (var i = 0; i < rects.Count; i++) rects[i] = ForceIntoArea(rects[i], to);

            if (!moved) return NoOverlaps(rects);
        }

        return false;
    }

    /// <summary>
    /// Verteilt den Ueberlapp auf beide Seiten — je nachdem, wie viel Platz
    /// jede Seite noch hat. Steht eine Seite am Rand, uebernimmt die andere den
    /// ganzen Weg; sonst blockieren sich Bereiche am Bildschirmrand gegenseitig.
    /// </summary>
    private static (double A, double B) Verteilen(double overlap, double platzA, double platzB)
    {
        platzA = Math.Max(0, platzA);
        platzB = Math.Max(0, platzB);

        var haelfte = overlap / 2;
        if (platzA >= haelfte && platzB >= haelfte) return (haelfte, haelfte);

        // Eine Seite kann nicht (genug) ausweichen → die andere macht mehr.
        var vonA = Math.Min(platzA, overlap);
        var vonB = Math.Min(platzB, overlap - vonA);
        if (vonA + vonB < overlap)
        {
            // Beide am Anschlag: soweit es geht, den Rest traegt A.
            vonA = Math.Min(platzA, overlap - vonB);
        }
        return (vonA, vonB);
    }

    private static LayoutRect Offset(LayoutRect r, double dx, double dy)
        => new() { X = r.X + dx, Y = r.Y + dy, Width = r.Width, Height = r.Height };

    /// Haelt den Bereich vollstaendig innerhalb der Flaeche (ohne Groessenaenderung).
    private static LayoutRect ForceIntoArea(LayoutRect r, Area area)
        => new()
        {
            X = Clamp(r.X, area.X, Math.Max(area.X, area.X + area.Width - r.Width)),
            Y = Clamp(r.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - r.Height)),
            Width = r.Width,
            Height = r.Height
        };

    private static bool NoOverlaps(List<LayoutRect> rects)
    {
        for (var i = 0; i < rects.Count; i++)
        for (var j = i + 1; j < rects.Count; j++)
            if (Overlaps(rects[i], rects[j])) return false;
        return true;
    }

    private static List<LayoutRect> Round(List<LayoutRect> rects)
        => rects.Select(r => new LayoutRect
        {
            X = Math.Round(r.X), Y = Math.Round(r.Y),
            Width = Math.Round(r.Width), Height = Math.Round(r.Height)
        }).ToList();

    /// <summary>
    /// Ordnet alle Bereiche an einem gedachten Raster an: zeilenweise, mit
    /// ueberall GLEICHEM Zwischenraum. Die Groessen bleiben dabei unveraendert —
    /// es wird ausschliesslich verschoben.
    ///
    /// Aufgebaut wird von UNTEN RECHTS nach oben links: dort sitzen die Bereiche
    /// ueblicherweise, und die linke Bildschirmhaelfte bleibt fuer die Symbole
    /// des Desktops frei. Die bisherige Reihenfolge bleibt erhalten, damit die
    /// Anordnung vertraut wirkt.
    /// </summary>
    public static IReadOnlyList<LayoutRect> ArrangeOnGrid(
        IReadOnlyList<LayoutRect> items, Area area, double gap)
    {
        if (items.Count == 0) return items;
        if (area.Width <= 0 || area.Height <= 0) return items;

        // Reihenfolge von unten rechts aus: unterste Zeile zuerst, darin von rechts.
        var order = Enumerable.Range(0, items.Count)
            .OrderByDescending(i => (int)Math.Floor(items[i].Y / RowBand))
            .ThenByDescending(i => items[i].X)
            .ToList();

        var links = area.X + gap;
        var rechts = area.X + area.Width - gap;
        var unten = area.Y + area.Height - gap;

        // 1. Zeilen bilden. Bewusst getrennt vom Platzieren: nur so steht die
        //    Hoehe einer Zeile fest, bevor ihre Bereiche gesetzt werden — und
        //    nur dann liefert ein zweiter Aufruf dasselbe Ergebnis.
        var zeilen = new List<List<int>>();
        var aktuelle = new List<int>();
        var breite = 0.0;

        foreach (var index in order)
        {
            var benoetigt = items[index].Width + (aktuelle.Count > 0 ? gap : 0);
            if (aktuelle.Count > 0 && breite + benoetigt > rechts - links)
            {
                zeilen.Add(aktuelle);
                aktuelle = new List<int>();
                breite = 0;
                benoetigt = items[index].Width;
            }
            aktuelle.Add(index);
            breite += benoetigt;
        }
        if (aktuelle.Count > 0) zeilen.Add(aktuelle);

        // 2. Platzieren: von unten nach oben, in jeder Zeile von rechts nach links.
        //    Innerhalb einer Zeile stehen alle Bereiche auf gleicher Hoehe.
        var result = new LayoutRect[items.Count];
        var zeilenUnterkante = unten;

        foreach (var zeile in zeilen)
        {
            var zeilenHoehe = zeile.Max(i => items[i].Height);
            var y = zeilenUnterkante - zeilenHoehe;
            var x = rechts;

            foreach (var index in zeile)
            {
                var item = items[index];
                result[index] = new LayoutRect
                {
                    X = Math.Round(x - item.Width),
                    Y = Math.Round(y),
                    Width = item.Width,   // Groesse bleibt unangetastet
                    Height = item.Height
                };
                x -= item.Width + gap;
            }

            zeilenUnterkante -= zeilenHoehe + gap;
        }

        // Ragt die oberste Zeile hinaus, alles gemeinsam nach unten schieben —
        // ohne den gleichmaessigen Abstand zu veraendern.
        var oben = result.Min(r => r.Y);
        var ueberstand = area.Y + gap - oben;
        if (ueberstand > 0)
        {
            var platzUnten = (area.Y + area.Height - gap) - result.Max(r => r.Y + r.Height);
            var verschiebung = Math.Min(ueberstand, Math.Max(0, platzUnten));
            if (verschiebung > 0)
                for (var i = 0; i < result.Length; i++)
                    result[i] = new LayoutRect
                    {
                        X = result[i].X, Y = Math.Round(result[i].Y + verschiebung),
                        Width = result[i].Width, Height = result[i].Height
                    };
        }

        return result;
    }


    private static double Clamp(double value, double min, double max)
        => max < min ? min : Math.Min(Math.Max(value, min), max);
}
