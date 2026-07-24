using System.IO;
using ISDesk.Models;
using ISDesk.Services;
using ISDesk.ViewModels;

namespace ISDesk.Tests;

/// Tabs laden erst, wenn sie angezeigt werden (spart Speicher/Watcher).
/// Diese Tests sichern ab, dass dabei nichts verloren geht — vor allem nicht
/// die manuelle Icon-Reihenfolge (TabConfig.Order) und die Live-Suche.
public class LazyTabLoadingTests : IDisposable
{
    private readonly string _root;

    public LazyTabLoadingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "isdesk_lazy_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { /* egal */ }
    }

    private string MakeTabFolder(string name, params string[] files)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        foreach (var file in files) File.WriteAllText(Path.Combine(folder, file), "x");
        return folder;
    }

    private static FenceViewModel Build(params TabConfig[] tabs)
    {
        var config = new FenceConfig { Title = "Test" };
        config.Tabs.AddRange(tabs);
        return new FenceViewModel(config, Path.GetTempPath());
    }

    [Fact]
    public void NurDerSichtbareTabWirdGeladen()
    {
        var vm = Build(
            new TabConfig { Title = "A", FolderPath = MakeTabFolder("A", "a1.txt", "a2.txt") },
            new TabConfig { Title = "B", FolderPath = MakeTabFolder("B", "b1.txt") });

        vm.ActivateVisibleTab();

        Assert.True(vm.Tabs[0].IsLoaded);
        Assert.Equal(2, vm.Tabs[0].Items.Count);
        Assert.False(vm.Tabs[1].IsLoaded);
        Assert.Empty(vm.Tabs[1].Items);
    }

    [Fact]
    public void TabWechselLaedtNachUndGibtDenVorherigenFrei()
    {
        var vm = Build(
            new TabConfig { Title = "A", FolderPath = MakeTabFolder("A", "a1.txt", "a2.txt") },
            new TabConfig { Title = "B", FolderPath = MakeTabFolder("B", "b1.txt") });
        vm.ActivateVisibleTab();

        vm.ActiveTab = vm.Tabs[1];

        Assert.True(vm.Tabs[1].IsLoaded);
        Assert.Single(vm.Tabs[1].Items);
        Assert.False(vm.Tabs[0].IsLoaded);
        Assert.Empty(vm.Tabs[0].Items);
    }

    [Fact]
    public void ManuelleReihenfolgeUeberlebtDasEntladen()
    {
        var tabA = new TabConfig { Title = "A", FolderPath = MakeTabFolder("A", "a1.txt", "a2.txt", "a3.txt") };
        var vm = Build(tabA, new TabConfig { Title = "B", FolderPath = MakeTabFolder("B", "b1.txt") });
        vm.ActivateVisibleTab();

        // Manuell umsortieren: a3 nach vorn.
        vm.Tabs[0].ReorderTo(Path.Combine(tabA.FolderPath, "a3.txt"),
                             Path.Combine(tabA.FolderPath, "a1.txt"));
        var order = new List<string>(tabA.Order);
        Assert.Equal("a3.txt", order[0]);

        vm.ActiveTab = vm.Tabs[1];   // A wird entladen
        Assert.Equal(order, tabA.Order);

        vm.ActiveTab = vm.Tabs[0];   // A wird neu geladen
        Assert.Equal(order, tabA.Order);
        Assert.Equal("a3.txt", vm.Tabs[0].Items[0].DisplayName);
    }

    [Fact]
    public void ReihenfolgeBleibtWennDerOrdnerFehlt()
    {
        var tab = new TabConfig
        {
            Title = "Weg",
            FolderPath = Path.Combine(_root, "gibtsnicht"),
            Order = { "merken.txt" }
        };
        var vm = Build(tab);

        vm.ActivateVisibleTab();

        Assert.Empty(vm.Tabs[0].Items);
        Assert.Equal(new[] { "merken.txt" }, tab.Order);
    }

    [Fact]
    public void TabZaehlerStimmtAuchOhneGeladenenTab()
    {
        var vm = Build(
            new TabConfig { Title = "A", FolderPath = MakeTabFolder("A", "a1.txt") },
            new TabConfig { Title = "B", FolderPath = MakeTabFolder("B", "b1.txt", "b2.txt", "b3.txt") });
        vm.ActivateVisibleTab();

        Assert.Equal(1, vm.Tabs[0].ItemCount);
        Assert.False(vm.Tabs[1].IsLoaded);
        Assert.Equal(3, vm.Tabs[1].ItemCount);
    }

    [Fact]
    public void SucheFindetAuchInNichtGeladenenTabs()
    {
        var vm = Build(
            new TabConfig { Title = "A", FolderPath = MakeTabFolder("A", "a1.txt") },
            new TabConfig { Title = "B", FolderPath = MakeTabFolder("B", "rechnung.pdf") });
        vm.ActivateVisibleTab();

        try
        {
            SearchService.SetTerm("rechnung");
            Assert.False(vm.Tabs[0].HasSearchMatch);
            Assert.False(vm.Tabs[1].IsLoaded);
            Assert.True(vm.Tabs[1].HasSearchMatch); // Reiter-Markierung ohne Laden

            SearchService.SetTerm("");
            Assert.False(vm.Tabs[1].HasSearchMatch);
        }
        finally
        {
            SearchService.SetTerm("");
            vm.DisposeTabs();
        }
    }
}
