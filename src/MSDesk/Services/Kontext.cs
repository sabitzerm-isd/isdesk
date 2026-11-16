namespace MSDesk.Services;

/// <summary>
/// Firma/Privat: welche Bereiche in welcher Ansicht sichtbar sind.
///
/// Reine Regel ohne Fenster, damit sie pruefbar ist. Unbekannte Werte (von
/// Hand bearbeitete Konfiguration, spaetere Fassung) gelten als „Immer" —
/// ein Bereich darf nie aus Versehen dauerhaft verschwinden.
/// </summary>
public static class Kontext
{
    public const string Immer = "Immer";
    public const string Firma = "Firma";
    public const string Privat = "Privat";

    /// Ist ein Bereich mit dieser Einstellung in dieser Ansicht sichtbar?
    public static bool SollSichtbar(string? sichtbarkeit, string? ansicht)
    {
        var s = Normalisieren(sichtbarkeit);
        return s == Immer || s == AnsichtNormalisieren(ansicht);
    }

    /// Bereichs-Einstellung: Immer, Firma oder Privat — sonst Immer.
    public static string Normalisieren(string? sichtbarkeit)
        => sichtbarkeit is Firma or Privat ? sichtbarkeit : Immer;

    /// Ansicht: Firma oder Privat — sonst Firma.
    public static string AnsichtNormalisieren(string? ansicht)
        => ansicht == Privat ? Privat : Firma;

    /// Die jeweils andere Ansicht.
    public static string Andere(string? ansicht)
        => AnsichtNormalisieren(ansicht) == Firma ? Privat : Firma;
}
