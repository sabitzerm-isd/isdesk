using MSDesk.Models;

namespace MSDesk.Services;

/// <summary>
/// Entscheidet, wo die Bereiche nach einem Bildschirmwechsel liegen sollen.
///
/// Bewusst als reine Rechnung ohne Fenster: nur so laesst sich der komplette
/// Ablauf (anstecken → abstecken → verschieben → wieder anstecken) vollstaendig
/// durchspielen und absichern. Der <see cref="FenceManager"/> setzt das Ergebnis
/// anschliessend nur noch um.
/// </summary>
public static class DisplaySwitchPlan
{
    /// Ein Bereich mit seiner aktuellen Lage und allen gemerkten Anordnungen.
    public sealed record Fence(LayoutRect Current, IReadOnlyDictionary<string, LayoutRect> Layouts);

    /// Ergebnis: neue Lage aller Bereiche und die Begruendung.
    public sealed record Plan(IReadOnlyList<LayoutRect> Positions, PlanKind Kind);

    public enum PlanKind
    {
        /// Fuer jeden Bereich lag eine Anordnung vor — exakt wiederhergestellt.
        Restored,

        /// Die bestehende Lage passt unveraendert (z. B. Beamer kommt dazu).
        Unchanged,

        /// Neu abgeleitet, weil (noch) keine vollstaendige Anordnung vorlag.
        Derived
    }

    /// <summary>
    /// <paramref name="virtualArea"/> = Gesamtflaeche ueber alle Bildschirme,
    /// <paramref name="workArea"/> = Arbeitsflaeche des Hauptbildschirms
    /// (dorthin wird zusammengefuehrt, wenn Platz fehlt).
    /// </summary>
    public static Plan Compute(IReadOnlyList<Fence> fences, string key,
                               LayoutTransfer.Area virtualArea, LayoutTransfer.Area workArea)
    {
        if (fences.Count == 0)
            return new Plan(Array.Empty<LayoutRect>(), PlanKind.Restored);

        // 1. Vollstaendig gemerkt? Dann EXAKT wiederherstellen — ohne jede
        //    Nachbearbeitung. Was von Hand eingerichtet wurde, bleibt so.
        if (fences.All(f => f.Layouts.ContainsKey(key)))
            return new Plan(fences.Select(f => Copy(f.Layouts[key])).ToList(), PlanKind.Restored);

        // 2. Passt die bestehende Lage unveraendert auf die verfuegbare Flaeche?
        //    Dann nichts anfassen (Beamer kommt dazu, Aufloesung waechst …).
        var aktuell = fences.Select(f => Copy(f.Current)).ToList();
        if (LayoutTransfer.FitsWithoutChange(aktuell, virtualArea))
            return new Plan(aktuell, PlanKind.Unchanged);

        // 3. Neu ableiten: bereits gemerkte Bereiche behalten ihre Lage, die
        //    uebrigen werden anteilig auf den Hauptbildschirm uebertragen.
        var ausgangslage = fences
            .Select(f => f.Layouts.TryGetValue(key, out var bekannt) ? Copy(bekannt) : Copy(f.Current))
            .ToList();

        var quelle = LayoutTransfer.Enclose(null, ausgangslage);
        if (quelle is not { } from) return new Plan(ausgangslage, PlanKind.Derived);

        var neu = LayoutTransfer.Arrange(ausgangslage, from, workArea);
        return new Plan(neu, PlanKind.Derived);
    }

    private static LayoutRect Copy(LayoutRect r)
        => new() { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height };
}
