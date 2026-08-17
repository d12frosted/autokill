using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class PhraseTests
{
    [Fact]
    public void NothingReadsAsNothing()
    {
        Assert.Equal(string.Empty, Phrases.List([]));
    }

    [Fact]
    public void OneNameIsJustTheName()
    {
        Assert.Equal("kokkine petalouda", Phrases.List(["kokkine petalouda"]));
    }

    [Fact]
    public void TwoNamesAreJoinedWithAnd()
    {
        Assert.Equal("a and b", Phrases.List(["a", "b"]));
    }

    [Fact]
    public void MoreNamesTakeCommasUntilTheLast()
    {
        Assert.Equal("a, b and c", Phrases.List(["a", "b", "c"]));
    }

    /// <summary>
    /// Mobs that share a field usually share most of their name, and saying it
    /// three times fills a line with the one word that carries no information.
    /// </summary>
    [Fact]
    public void AWordEveryNameEndsWithIsSaidOnce()
    {
        Assert.Equal(
            "ianthine, kokkine and kyane petalouda",
            Phrases.Kinds(["ianthine petalouda", "kokkine petalouda", "kyane petalouda"]));
    }

    [Fact]
    public void SeveralSharedWordsAllMoveToTheEnd()
    {
        Assert.Equal(
            "lesser and greater dhalmel calf",
            Phrases.Kinds(["lesser dhalmel calf", "greater dhalmel calf"]));
    }

    [Fact]
    public void NamesWithNothingInCommonAreLeftAlone()
    {
        Assert.Equal("wolf and bear", Phrases.Kinds(["wolf", "bear"]));
    }

    [Fact]
    public void OneNameKeepsAllOfItself()
    {
        Assert.Equal("kokkine petalouda", Phrases.Kinds(["kokkine petalouda"]));
    }

    /// <summary>
    /// Nothing is folded away that would leave a name as only its shared part.
    /// "and petalouda" names nothing.
    /// </summary>
    [Fact]
    public void ANameThatIsOnlyTheSharedPartStopsTheFolding()
    {
        Assert.Equal(
            "petalouda and kyane petalouda",
            Phrases.Kinds(["petalouda", "kyane petalouda"]));
    }

    [Fact]
    public void FoldingMatchesWholeWordsOnly()
    {
        Assert.Equal("gigantoad and toad", Phrases.Kinds(["gigantoad", "toad"]));
    }
}
