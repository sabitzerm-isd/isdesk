using MSDesk.Models;
using MSDesk.Services;

namespace MSDesk.Tests;

/// <summary>
/// Die Entscheidung „jetzt sichern oder nicht" ist der Kern der Automatik.
/// Sie muss stimmen, ohne dass jemand hinschaut — deshalb hier geprueft,
/// getrennt vom Zeitgeber.
/// </summary>
public class AutoBackupServiceTests
{
    private static readonly DateTime Jetzt = new(2026, 7, 28, 20, 0, 0, DateTimeKind.Utc);

    private static AppConfig Eingerichtet() => new()
    {
        AutoBackupDaily = true,
        AutoBackupFolder = @"C:\Cloud\MSDesk-Sicherungen"
    };

    [Fact]
    public void NochNieGesichert_IstFaellig()
    {
        var config = Eingerichtet();
        Assert.Null(config.LastAutoBackupUtc);
        Assert.True(AutoBackupService.IstFaellig(config, Jetzt));
    }

    [Fact]
    public void GeradeEbenGesichert_IstNichtFaellig()
    {
        var config = Eingerichtet();
        config.LastAutoBackupUtc = Jetzt.AddMinutes(-5);

        Assert.False(AutoBackupService.IstFaellig(config, Jetzt));
    }

    [Fact]
    public void KnappUnterEinemTag_IstNochNichtFaellig()
    {
        var config = Eingerichtet();
        config.LastAutoBackupUtc = Jetzt.AddHours(-23).AddMinutes(-59);

        Assert.False(AutoBackupService.IstFaellig(config, Jetzt));
    }

    [Fact]
    public void GenauEinTag_IstFaellig()
    {
        var config = Eingerichtet();
        config.LastAutoBackupUtc = Jetzt - AutoBackupService.Abstand;

        Assert.True(AutoBackupService.IstFaellig(config, Jetzt));
    }

    [Fact]
    public void RechnerWarLangeAus_IstFaellig()
    {
        // Der haeufigste Fall: ueber das Wochenende nicht eingeschaltet.
        var config = Eingerichtet();
        config.LastAutoBackupUtc = Jetzt.AddDays(-4);

        Assert.True(AutoBackupService.IstFaellig(config, Jetzt));
    }

    [Fact]
    public void Abgeschaltet_IstNieFaellig()
    {
        var config = Eingerichtet();
        config.AutoBackupDaily = false;

        Assert.False(AutoBackupService.IstFaellig(config, Jetzt));
    }

    [Fact]
    public void OhneZielordner_IstNieFaellig()
    {
        // Ohne Ziel gibt es nichts zu tun — und vor allem keine Meldung.
        var config = Eingerichtet();
        config.AutoBackupFolder = null;
        Assert.False(AutoBackupService.IstFaellig(config, Jetzt));

        config.AutoBackupFolder = "   ";
        Assert.False(AutoBackupService.IstFaellig(config, Jetzt));
    }

    [Fact]
    public void ZeitpunktInDerZukunft_IstFaellig()
    {
        // Kommt bei verstellter Uhr oder von Hand bearbeiteter Konfiguration
        // vor. Ohne Fang bliebe die Sicherung danach dauerhaft aus.
        var config = Eingerichtet();
        config.LastAutoBackupUtc = Jetzt.AddDays(30);

        Assert.True(AutoBackupService.IstFaellig(config, Jetzt));
    }

    [Fact]
    public void StandardIstEingeschaltet()
    {
        // Eine Sicherung, an die man denken muss, ist keine.
        Assert.True(new AppConfig().AutoBackupDaily);
    }
}
