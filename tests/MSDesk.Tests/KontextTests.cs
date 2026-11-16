using MSDesk.Models;
using MSDesk.Services;

namespace MSDesk.Tests;

/// Ansicht Firma/Privat: welcher Bereich wann sichtbar ist.
public class KontextTests
{
    [Theory]
    [InlineData("Immer",  "Firma",  true)]
    [InlineData("Immer",  "Privat", true)]
    [InlineData("Firma",  "Firma",  true)]
    [InlineData("Firma",  "Privat", false)]
    [InlineData("Privat", "Privat", true)]
    [InlineData("Privat", "Firma",  false)]
    public void SollSichtbar_Regel(string sichtbarkeit, string ansicht, bool erwartet)
        => Assert.Equal(erwartet, Kontext.SollSichtbar(sichtbarkeit, ansicht));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("firma")]
    [InlineData("Unsinn")]
    public void UnbekannteEinstellung_GiltAlsImmer(string? wert)
    {
        // Ein Bereich darf nie aus Versehen dauerhaft verschwinden.
        Assert.True(Kontext.SollSichtbar(wert, Kontext.Firma));
        Assert.True(Kontext.SollSichtbar(wert, Kontext.Privat));
    }

    [Fact]
    public void Standardwerte_AllesSichtbarInFirma()
    {
        Assert.Equal(Kontext.Immer, new FenceConfig().Sichtbarkeit);
        Assert.Equal(Kontext.Firma, new AppConfig().Ansicht);
    }

    [Fact]
    public void Andere_Wechselt()
    {
        Assert.Equal(Kontext.Privat, Kontext.Andere(Kontext.Firma));
        Assert.Equal(Kontext.Firma, Kontext.Andere(Kontext.Privat));
        Assert.Equal(Kontext.Privat, Kontext.Andere(null));
    }
}
