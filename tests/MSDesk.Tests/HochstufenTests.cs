using MSDesk.Interop;

namespace MSDesk.Tests;

/// Bleibt ein Bereich bei „Desktop anzeigen" unten, geht MSDesk die Stufen der
/// Reihe nach durch — und hoert auf, sobald eine gewirkt hat.
public class HochstufenTests
{
    [Fact]
    public void Regelfall_NurErsterSchritt()
    {
        var gelaufen = new List<int>();
        Assert.Equal(0, DesktopPinning.ErsteWirksame(() => true, () => gelaufen.Add(0), () => gelaufen.Add(1)));
        Assert.Equal(new[] { 0 }, gelaufen);
    }

    [Fact]
    public void Klemmt_GehtWeiterBisEsWirkt()
    {
        var oben = false;
        var gelaufen = new List<int>();
        var stufe = DesktopPinning.ErsteWirksame(() => oben,
            () => gelaufen.Add(0),
            () => { gelaufen.Add(1); oben = true; },
            () => gelaufen.Add(2));
        Assert.Equal(1, stufe);
        Assert.Equal(new[] { 0, 1 }, gelaufen);
    }

    [Fact]
    public void NichtsHilft_MeldetMinusEins()
        => Assert.Equal(-1, DesktopPinning.ErsteWirksame(() => false, () => { }, () => { }));
}
