using System.IO;
using System.Linq;
using ISDesk.Services;

namespace ISDesk.Tests;

public class FolderContentsTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ISDeskFC_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ListVisibleEntries_FiltersHiddenAndSpecial_AndSortsFoldersFirst()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "ZOrdner"));
            Directory.CreateDirectory(Path.Combine(dir, "AOrdner"));
            File.WriteAllText(Path.Combine(dir, "banane.txt"), "x");
            File.WriteAllText(Path.Combine(dir, "apfel.txt"), "x");

            var hidden = Path.Combine(dir, "geheim.txt");
            File.WriteAllText(hidden, "x");
            File.SetAttributes(hidden, FileAttributes.Hidden);
            File.WriteAllText(Path.Combine(dir, "desktop.ini"), "x");

            var names = FolderContents.ListVisibleEntries(dir).Select(Path.GetFileName).ToArray();

            Assert.Equal(new[] { "AOrdner", "ZOrdner", "apfel.txt", "banane.txt" }, names);
            Assert.DoesNotContain("geheim.txt", names);
            Assert.DoesNotContain("desktop.ini", names);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("Editor.lnk", "Editor")]
    [InlineData("Startseite.url", "Startseite")]
    [InlineData("App.appref-ms", "App")]
    [InlineData("Notiz.txt", "Notiz.txt")]
    [InlineData("Archiv.tar.gz", "Archiv.tar.gz")]
    public void GetDisplayName_AppliesShortcutRules(string fileName, string expected)
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, fileName);
            File.WriteAllText(path, "x");
            Assert.Equal(expected, FolderContents.GetDisplayName(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void GetDisplayName_Folder_ReturnsFolderName()
    {
        var dir = NewTempDir();
        try
        {
            var sub = Path.Combine(dir, "MeinOrdner");
            Directory.CreateDirectory(sub);
            Assert.Equal("MeinOrdner", FolderContents.GetDisplayName(sub));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ListVisibleEntries_MissingFolder_ReturnsEmpty()
    {
        var missing = Path.Combine(Path.GetTempPath(), "ISDeskFC_missing_" + Guid.NewGuid().ToString("N"));
        Assert.Empty(FolderContents.ListVisibleEntries(missing));
    }
}
