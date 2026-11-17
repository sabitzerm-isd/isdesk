namespace MSDesk.Services;

/// <summary>
/// Es laeuft immer nur EIN Einsammel- oder Einordnen-Lauf zur Zeit.
///
/// Zwei Laeufe nebeneinander (Doppelklick auf „Aktualisieren", Automatik und
/// Knopf gleichzeitig) haben sich gegenseitig fuer Doppelte gehalten: Der
/// zweite fand die eben vom ersten verschobene Datei am Ziel, hielt sie fuer
/// eine alte Fassung und legte sie in den Papierkorb.
///
/// Die Sperre ist wiedereintrittsfaehig (Monitor): ein Lauf darf intern einen
/// anderen aufrufen (Einordnen raeumt am Ende Doppelte auf).
/// </summary>
public static class Einsammeln
{
    private static readonly object Sperre = new();

    /// Automatik: ist schon ein Lauf aktiv, wird dieser hier still uebersprungen.
    /// Von Hand: kurz warten, statt den Knopfdruck zu verschlucken.
    public static bool Betreten(int wartenMs = 0) => Monitor.TryEnter(Sperre, wartenMs);

    public static void Verlassen() => Monitor.Exit(Sperre);
}
