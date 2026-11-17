using MSDesk.Services;

namespace MSDesk.Tests;

/// „Bereich entfernen" mit Ordnern darf nie Ordner treffen, die ein anderer
/// Bereich noch nutzt (nach Reiter abtrennen / verschieben).
public class RemoveFenceFolderTests
{
    private const string Basis = @"D:\Fences";

    [Fact]
    public void Standardfall_GanzerBereichsordner()
    {
        var r = FenceManager.OrdnerZumEntfernen(
            new[] { @"D:\Fences\KI\Allgemein", @"D:\Fences\KI\Bilder" },
            new[] { @"D:\Fences\Programme\Allgemein" }, Basis);

        Assert.Equal(new[] { @"D:\Fences\KI" }, r.ToArray());
    }

    [Fact]
    public void AbgetrennterReiter_BleibtStehen()
    {
        // „Bilder" wurde aus KI abgetrennt, sein Ordner liegt weiter unter KI.
        var r = FenceManager.OrdnerZumEntfernen(
            new[] { @"D:\Fences\KI\Allgemein" },
            new[] { @"D:\Fences\KI\Bilder" }, Basis);

        Assert.Equal(new[] { @"D:\Fences\KI\Allgemein" }, r.ToArray());
    }

    [Fact]
    public void GemeinsamerOrdner_WirdNichtEntfernt()
    {
        var r = FenceManager.OrdnerZumEntfernen(
            new[] { @"D:\Fences\KI\Allgemein" },
            new[] { @"D:\Fences\KI\Allgemein" }, Basis);

        Assert.Empty(r);
    }

    [Fact]
    public void OrdnerAusserhalbDerBasis_WirdNieEntfernt()
    {
        var r = FenceManager.OrdnerZumEntfernen(
            new[] { @"C:\Users\x\Documents\Projekt" }, Array.Empty<string>(), Basis);

        Assert.Empty(r);
    }

    [Fact]
    public void UnterschiedlicheSchreibweise_ZaehltAlsBelegt()
    {
        var r = FenceManager.OrdnerZumEntfernen(
            new[] { @"D:\Fences\KI\Allgemein" },
            new[] { @"d:\fences\ki\bilder\" }, Basis);

        Assert.Equal(new[] { @"D:\Fences\KI\Allgemein" }, r.ToArray());
    }
}
