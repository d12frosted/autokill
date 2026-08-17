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
}
