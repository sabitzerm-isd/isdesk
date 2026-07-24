using MSDesk.Services;
using Xunit;

namespace MSDesk.Tests;

public class FileCategoriesTests
{
    [Fact]
    public void ExactExtension_Matches()
    {
        Assert.True(FileCategories.MatchesExact(new[] { "sza" }, "sza"));
        Assert.True(FileCategories.MatchesExact(new[] { "zip", "7z" }, "7z"));
        Assert.False(FileCategories.MatchesExact(new[] { "sza" }, "ifc"));
    }

    [Fact]
    public void Category_ExpandsToManyExtensions()
    {
        Assert.True(FileCategories.MatchesCategory(new[] { "bilder" }, "png"));
        Assert.True(FileCategories.MatchesCategory(new[] { "bilder" }, "jpg"));
        Assert.True(FileCategories.MatchesCategory(new[] { "office" }, "docx"));
        Assert.True(FileCategories.MatchesCategory(new[] { "office" }, "pdf"));
        Assert.False(FileCategories.MatchesCategory(new[] { "bilder" }, "sza"));
    }

    [Fact]
    public void Category_IsNotMatchedByExact()
    {
        // "bilder" ist eine Kategorie, keine reale Endung — MatchesExact darf NICHT anschlagen
        Assert.False(FileCategories.MatchesExact(new[] { "bilder" }, "png"));
    }

    [Fact]
    public void Matches_CombinesBoth()
    {
        Assert.True(FileCategories.Matches(new[] { "sza" }, "sza"));       // exakt
        Assert.True(FileCategories.Matches(new[] { "bilder" }, "png"));    // Kategorie
        Assert.False(FileCategories.Matches(new[] { "sza", "bilder" }, "docx"));
    }

    [Fact]
    public void EmptyExtension_NeverMatches()
    {
        Assert.False(FileCategories.Matches(new[] { "sza" }, ""));
    }
}

public class FolderRuleTests
{
    [Theory]
    [InlineData("ordner")]
    [InlineData("Ordner")]
    [InlineData("verzeichnis")]
    [InlineData("folder")]
    public void Ordner_Schluesselwoerter_werden_erkannt(string rule)
    {
        Assert.True(FileCategories.IsFolderRule(new[] { rule }));
    }

    [Fact]
    public void Ordner_Regel_neben_anderen_Regeln()
    {
        Assert.True(FileCategories.IsFolderRule(new[] { "sza", "ordner" }));
    }

    [Fact]
    public void Ohne_Ordner_Schluesselwort_keine_Ordner_Regel()
    {
        Assert.False(FileCategories.IsFolderRule(new[] { "sza", "bilder" }));
        Assert.False(FileCategories.IsFolderRule(Array.Empty<string>()));
    }

    [Fact]
    public void Ordner_ist_keine_Dateiendung()
    {
        // "ordner" darf nicht versehentlich als Endung .ordner gelten
        Assert.False(FileCategories.MatchesExact(new[] { "ordner" }, "sza"));
        Assert.False(FileCategories.MatchesCategory(new[] { "ordner" }, "png"));
    }
}
