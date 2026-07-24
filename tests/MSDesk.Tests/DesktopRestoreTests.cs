using MSDesk.Models;
using MSDesk.Services;
using Xunit;

namespace MSDesk.Tests;

public class DesktopRestoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MSDeskRestore_" + Guid.NewGuid().ToString("N"));

    private AppConfigSource BuildConfig(params string[] tabFolders)
    {
        var fence = new FenceConfig { Title = "Test" };
        foreach (var folder in tabFolders)
        {
            Directory.CreateDirectory(folder);
            fence.Tabs.Add(new TabConfig { Title = Path.GetFileName(folder), FolderPath = folder });
        }
        return new AppConfigSource(new[] { fence });
    }

    [Fact]
    public void Count_zaehlt_Dateien_aller_Tabs()
    {
        var a = Path.Combine(_root, "A");
        var b = Path.Combine(_root, "B");
        var config = BuildConfig(a, b);
        File.WriteAllText(Path.Combine(a, "eins.txt"), "x");
        File.WriteAllText(Path.Combine(a, "zwei.txt"), "x");
        File.WriteAllText(Path.Combine(b, "drei.txt"), "x");

        Assert.Equal(3, DesktopRestore.Count(config));
    }

    [Fact]
    public void Count_ist_null_wenn_Ordner_fehlt()
    {
        var fence = new FenceConfig { Title = "Test" };
        fence.Tabs.Add(new TabConfig { Title = "Weg", FolderPath = Path.Combine(_root, "gibtsnicht") });

        Assert.Equal(0, DesktopRestore.Count(new AppConfigSource(new[] { fence })));
    }

    [Fact]
    public void RestoreAll_leert_die_Bereichsordner()
    {
        var a = Path.Combine(_root, "A");
        var config = BuildConfig(a);
        File.WriteAllText(Path.Combine(a, "verknuepfung.url"), "x");

        var (moved, failed) = DesktopRestore.RestoreAll(config);

        Assert.Equal(1, moved);
        Assert.Equal(0, failed);
        Assert.Empty(Directory.GetFiles(a));

        // Aufraeumen: die Datei liegt jetzt auf dem echten Desktop
        var onDesktop = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "verknuepfung.url");
        if (File.Exists(onDesktop)) File.Delete(onDesktop);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
        catch (Exception) { /* Temp-Ordner */ }
    }
}
