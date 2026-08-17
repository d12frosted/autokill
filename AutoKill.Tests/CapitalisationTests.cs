using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class CapitalisationTests
{
    [Fact]
    public void NothingStaysNothing()
    {
        Assert.Equal(string.Empty, Phrases.Capitalise(string.Empty));
    }

    [Fact]
    public void EveryWordStarts()
    {
        Assert.Equal("Kokkine Petalouda", Phrases.Capitalise("kokkine petalouda"));
    }

    [Fact]
    public void WordsThatOnlyJoinStayDown()
    {
        Assert.Equal("Eye of the Storm", Phrases.Capitalise("eye of the storm"));
    }

    [Fact]
    public void AJoiningWordStillStartsASentence()
    {
        Assert.Equal("The Winged", Phrases.Capitalise("the winged"));
    }

    /// <summary>
    /// Only the first letter is touched. Numerals and names that are already
    /// shouting are how the game wrote them.
    /// </summary>
    [Fact]
    public void TheRestOfAWordIsLeftAlone()
    {
        Assert.Equal("Ixali Boldwing III", Phrases.Capitalise("Ixali boldwing III"));
    }

    [Fact]
    public void ExtraSpacesDoNotMoveAnything()
    {
        Assert.Equal("Ser  Adelphel", Phrases.Capitalise("ser  adelphel"));
    }
}
