using MSDesk.Models;
using MSDesk.Services;

namespace MSDesk.Tests;

/// <summary>
/// „Bekannte Symbole von selbst zurueckholen" ist die zurueckhaltende Schwester
/// der Ablage: Sie fasst NUR an, was MSDesk bereits kennt.
///
/// Der Ausloeser aus der Praxis: ein selbst gebautes Werkzeug, das oft neu
/// installiert wird, legt jedes Mal seine Verknuepfung erneut auf den Desktop —
/// obwohl sie laengst in einem Bereich liegt. Ohne Automatik sammelt sich das
/// an, bis jemand in den Optionen den Knopf drueckt.
/// </summary>
public class AutoReclaimTests
{
    [Fact]
    public void StandardIstEingeschaltet()
    {
        // Der Anwender soll den Nutzen haben, ohne ihn erst suchen zu muessen.
        Assert.True(new AppConfig().AutoReclaimKnown);
    }

    [Fact]
    public void AblageBleibtStandardmaessigAus()
    {
        // Wichtig: Die beiden sind NICHT dasselbe. Die Ablage saugt auch
        // Unbekanntes ein — das bleibt eine bewusste Entscheidung.
        Assert.False(new AppConfig().DesktopSweep);
    }

    [Fact]
    public void Ueberwachung_LaeuftSchonBeiEinerStufe()
    {
        // Frueher hing die Desktop-Ueberwachung allein an der Ablage. Wer sie
        // abschaltete, verlor das Zurueckholen still mit.
        var config = new AppConfig { DesktopSweep = false, AutoReclaimKnown = true };
        Assert.True(DesktopSweeper.Gewuenscht(config));

        config.DesktopSweep = true;
        config.AutoReclaimKnown = false;
        Assert.True(DesktopSweeper.Gewuenscht(config));
    }

    [Fact]
    public void Ueberwachung_RuhtNurWennBeidesAus()
    {
        var config = new AppConfig { DesktopSweep = false, AutoReclaimKnown = false };
        Assert.False(DesktopSweeper.Gewuenscht(config));
    }
}
