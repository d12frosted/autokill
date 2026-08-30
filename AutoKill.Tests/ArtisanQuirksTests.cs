using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class ArtisanQuirksTests
{
    [Fact]
    public void TheVersionThatLosesListEditsIsKnownToLoseThem()
    {
        Assert.True(ArtisanQuirks.LosesListEdits(new Version(4, 0, 5, 18)));
        Assert.True(ArtisanQuirks.LosesListEdits(new Version(4, 0, 5, 17)));
    }

    [Fact]
    public void ANewerArtisanIsNotAccusedOfIt()
    {
        Assert.False(ArtisanQuirks.LosesListEdits(new Version(4, 0, 5, 19)));
        Assert.False(ArtisanQuirks.LosesListEdits(new Version(4, 1, 0, 0)));
    }

    /// <summary>
    /// An Artisan that is not answering has no version, and guessing one either
    /// way would be making something up.
    /// </summary>
    [Fact]
    public void AnArtisanThatIsNotThereIsNotAccusedEither()
    {
        Assert.False(ArtisanQuirks.LosesListEdits(null));
        Assert.Contains("wrote its file", ArtisanQuirks.WhyEmpty(null));
    }

    [Fact]
    public void TheKnownVersionIsToldWhatItDoes()
    {
        var says = ArtisanQuirks.WhyEmpty(new Version(4, 0, 5, 18));

        Assert.Contains("Add all visible", says);
        Assert.DoesNotContain("still", says);
    }

    /// <summary>
    /// A later Artisan may or may not have fixed it. Somebody looking at a list
    /// that is full in one window and empty in the other is better served by the
    /// symptom than by a claim either way.
    /// </summary>
    [Fact]
    public void ALaterVersionIsGivenTheSymptomAndItsOwnNumber()
    {
        var says = ArtisanQuirks.WhyEmpty(new Version(4, 1, 2, 0));

        Assert.Contains("4.1.2", says);
        Assert.Contains("Add all visible", says);
    }
}
