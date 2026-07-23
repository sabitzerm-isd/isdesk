using ISDesk.Services;
using Xunit;

namespace ISDesk.Tests;

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
