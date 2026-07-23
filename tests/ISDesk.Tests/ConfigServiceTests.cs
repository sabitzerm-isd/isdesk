using System.IO;
using ISDesk.Models;
using ISDesk.Services;

namespace ISDesk.Tests;

public class ConfigServiceTests
{
    private static string NewTempConfigPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ISDeskTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "config.json");
    }

    [Fact]
    public void RoundTrip_PreservesValues_IncludingFencesAndTabs()
    {
        var path = NewTempConfigPath();

        var svc = new ConfigService(path);
        svc.Config.BaseFolder = @"D:\TestFences";
        svc.Config.DefaultOpacity = 0.6;
        svc.Config.DefaultBlur = false;

        var f1 = new FenceConfig
        {
            Id = Guid.NewGuid(),
            Title = "Bereich 1",
            X = 10, Y = 20, Width = 300, Height = 200,
            Opacity = 0.5, Blur = true, ActiveTab = 1
        };
        f1.Tabs.Add(new TabConfig { Title = "Allgemein", FolderPath = @"D:\TestFences\Bereich 1\Allgemein", IconSize = 32 });
        f1.Tabs.Add(new TabConfig { Title = "Projekte", FolderPath = @"D:\TestFences\Bereich 1\Projekte", IconSize = 48 });

        var f2 = new FenceConfig
        {
            Id = Guid.NewGuid(),
            Title = "Bereich 2",
            X = 100, Y = 120, Width = 260, Height = 180,
            Opacity = 0.8, Blur = false, ActiveTab = 0
        };
        f2.Tabs.Add(new TabConfig { Title = "Downloads", FolderPath = @"D:\TestFences\Bereich 2\Downloads", IconSize = 32 });

        svc.Config.Fences.Add(f1);
        svc.Config.Fences.Add(f2);
        svc.Save();

        var reloaded = new ConfigService(path);
        reloaded.Load();

        Assert.Equal(@"D:\TestFences", reloaded.Config.BaseFolder);
        Assert.Equal(0.6, reloaded.Config.DefaultOpacity);
        Assert.False(reloaded.Config.DefaultBlur);
        Assert.Equal(2, reloaded.Config.Fences.Count);

        var r1 = reloaded.Config.Fences[0];
        Assert.Equal(f1.Id, r1.Id);
        Assert.Equal("Bereich 1", r1.Title);
        Assert.Equal(10, r1.X);
        Assert.Equal(20, r1.Y);
        Assert.Equal(300, r1.Width);
        Assert.Equal(200, r1.Height);
        Assert.Equal(0.5, r1.Opacity);
        Assert.True(r1.Blur);
        Assert.Equal(1, r1.ActiveTab);
        Assert.Equal(2, r1.Tabs.Count);
        Assert.Equal("Projekte", r1.Tabs[1].Title);
        Assert.Equal(48, r1.Tabs[1].IconSize);

        var r2 = reloaded.Config.Fences[1];
        Assert.Equal("Bereich 2", r2.Title);
        Assert.Single(r2.Tabs);
        Assert.Equal("Downloads", r2.Tabs[0].Title);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefaults_AndWritesBadBackup()
    {
        var path = NewTempConfigPath();
        File.WriteAllText(path, "{ das ist kaputtes JSON <<< nicht parsebar ");

        var svc = new ConfigService(path);
        svc.Load();

        // Defaults geladen, kein Crash.
        Assert.Equal(@"D:\Fences", svc.Config.BaseFolder);
        Assert.Equal(0.75, svc.Config.DefaultOpacity);
        Assert.True(svc.Config.DefaultBlur);
        Assert.Empty(svc.Config.Fences);

        // Sicherungskopie der kaputten Datei existiert.
        var bad = Path.Combine(Path.GetDirectoryName(path)!, "config.bad.json");
        Assert.True(File.Exists(bad));
    }
}
