using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class RepopulationTests
{
    [Fact]
    public void NothingMeasuredIsNoEstimate()
    {
        Assert.Null(Repopulation.From([], []));
    }

    /// <summary>
    /// Two of anything is a coincidence. A circuit falling back to its default
    /// is better off than one following a number that came from a bad minute.
    /// </summary>
    [Fact]
    public void TooFewOfEitherKindIsNoEstimate()
    {
        Assert.Null(Repopulation.From([40, 41], [90, 95]));
    }

    [Fact]
    public void TheMiddleOfWhatWasTimedIsTheEstimate()
    {
        var expect = Expect([38, 40, 44], []);

        Assert.Equal(TimeSpan.FromSeconds(40), expect.Typical);
        Assert.Equal(3, expect.Samples);
        Assert.True(expect.Timed);
    }

    /// <summary>
    /// The median, not the mean: one measurement taken across a bad stretch
    /// should not drag the estimate after it.
    /// </summary>
    [Fact]
    public void OneWildMeasurementDoesNotMoveTheEstimate()
    {
        Assert.Equal(TimeSpan.FromSeconds(41), Expect([38, 40, 44, 41, 560], []).Typical);
    }

    /// <summary>
    /// Timing a spawn point has no travel in it, so it wins over the return
    /// trip even when the return trip has more behind it.
    /// </summary>
    [Fact]
    public void TimedSpawnPointsBeatReturnTrips()
    {
        var expect = Expect([40, 41, 42], [200, 210, 220, 230, 240]);

        Assert.Equal(TimeSpan.FromSeconds(41), expect.Typical);
        Assert.Equal(3, expect.Samples);
        Assert.True(expect.Timed);
    }

    /// <summary>
    /// Until there are enough of them. What was learnt the old way is still
    /// worth more than the default guess, and says what it is.
    /// </summary>
    [Fact]
    public void ReturnTripsCarryItUntilThereAreEnoughTimings()
    {
        var expect = Expect([40], [200, 210, 220]);

        Assert.Equal(TimeSpan.FromSeconds(210), expect.Typical);
        Assert.Equal(3, expect.Samples);
        Assert.False(expect.Timed);
    }

    /// <summary>An even count still has a middle, and it is between the two.</summary>
    [Fact]
    public void AnEvenCountIsSplitBetweenTheMiddleTwo()
    {
        Assert.Equal(TimeSpan.FromSeconds(43), Expect([40, 42, 44, 50], []).Typical);
    }

    private static Repopulation Expect(double[] timed, double[] returned)
    {
        var expect = Repopulation.From(timed, returned);
        Assert.NotNull(expect);
        return expect!.Value;
    }
}
