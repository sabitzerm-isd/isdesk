using System.IO;
using System.IO.Compression;
using MSDesk.Services;

namespace MSDesk.Tests;

/// <summary>
/// Wiederherstellen darf keine Arbeitsdatei vernichten: gleiche Dateien
/// bleiben unberuehrt, abweichende gehen vorher in den Papierkorb, und
/// geoeffnete Dateien werden VOR jeder Aenderung erkannt.
/// </summary>
public class RestoreFileTests : IDisposable
{
    private readonly string _root;
    private readonly string _zipPfad;

    public RestoreFileTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msdesk_restore_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        _zipPfad = Path.Combine(_root, "sicherung.zip");
        using var zip = ZipFile.Open(_zipPfad, ZipArchiveMode.Create);
        using (var w = new StreamWriter(zip.CreateEntry("Fences/Bereich/Tab/a.txt").Open())) w.Write("gesichert");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string Basis => Path.Combine(_root, "Basis");
    private string Ziel => Path.Combine(Basis, "Bereich", "Tab", "a.txt");

    [Fact]
    public void FehlendeDatei_WirdAngelegt()
    {
        using var zip = ZipFile.OpenRead(_zipPfad);
        Assert.True(BackupService.DateiZurueckspielen(zip.Entries[0], Ziel));
        Assert.Equal("gesichert", File.ReadAllText(Ziel));
    }

    [Fact]
    public void GleicheDatei_WirdNichtAngefasst()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Ziel)!);
        File.WriteAllText(Ziel, "gesichert");
        var zeit = File.GetLastWriteTimeUtc(Ziel).AddDays(-1);
        File.SetLastWriteTimeUtc(Ziel, zeit);

        using var zip = ZipFile.OpenRead(_zipPfad);
        Assert.False(BackupService.DateiZurueckspielen(zip.Entries[0], Ziel));
        Assert.Equal(zeit, File.GetLastWriteTimeUtc(Ziel));
    }

    [Fact]
    public void GeoeffneteAbweichendeDatei_WirdVorabGemeldet()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Ziel)!);
        File.WriteAllText(Ziel, "neuer bearbeitet");
        using var offen = new FileStream(Ziel, FileMode.Open, FileAccess.Read, FileShare.None);

        using var zip = ZipFile.OpenRead(_zipPfad);
        var gesperrt = BackupService.GesperrteZiele(zip, Basis);
        Assert.Single(gesperrt);
    }

    [Fact]
    public void GeoeffneteGleicheDatei_IstKeinHindernis()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Ziel)!);
        File.WriteAllText(Ziel, "gesichert");
        using var offen = new FileStream(Ziel, FileMode.Open, FileAccess.Read, FileShare.Read);

        using var zip = ZipFile.OpenRead(_zipPfad);
        Assert.Empty(BackupService.GesperrteZiele(zip, Basis));
    }
}
