using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class PaceTests
{
    [Fact]
    public void ThirtyInHalfAnHourIsSixtyAnHour()
    {
        Assert.Equal(60d, Pace.PerHour(30, TimeSpan.FromMinutes(30))!.Value, 3);
    }

    [Fact]
    public void NothingKilledIsAnHonestZero()
    {
        Assert.Equal(0d, Pace.PerHour(0, TimeSpan.FromMinutes(10))!.Value, 3);
    }

    [Fact]
    public void AMomentSaysNothing()
    {
        // One kill in twenty seconds reads as 180 an hour, which no run ever
        // delivers. Too short a stretch is no rate at all.
        Assert.Null(Pace.PerHour(1, TimeSpan.FromSeconds(20)));
        Assert.Null(Pace.PerHour(1, TimeSpan.Zero));
    }

    [Fact]
    public void AMinuteIsJustEnough()
    {
        Assert.Equal(120d, Pace.PerHour(2, TimeSpan.FromMinutes(1))!.Value, 3);
    }

    [Fact]
    public void TimeToGoScalesWhatTheRunHasShown()
    {
        // Ten of thirty in ten minutes: the other twenty cost twenty more.
        Assert.Equal(
            TimeSpan.FromMinutes(20),
            Pace.TimeToGo(done: 10, target: 30, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void NoPaceMeansNoPromise()
    {
        // Nothing done yet, or too little time watched, gives no basis for a
        // number, and a made-up one would sit on screen looking like a fact.
        Assert.Null(Pace.TimeToGo(done: 0, target: 30, TimeSpan.FromMinutes(10)));
        Assert.Null(Pace.TimeToGo(done: 5, target: 30, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void AMetTargetHasNothingToGo()
    {
        Assert.Equal(
            TimeSpan.Zero,
            Pace.TimeToGo(done: 30, target: 30, TimeSpan.FromMinutes(10)));
        Assert.Equal(
            TimeSpan.Zero,
            Pace.TimeToGo(done: 40, target: 30, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void RoughlySpeaksInMinutesAndHours()
    {
        Assert.Equal("under a minute", Pace.Roughly(TimeSpan.FromSeconds(40)));
        Assert.Equal("14 min", Pace.Roughly(TimeSpan.FromMinutes(14)));
        Assert.Equal("1 h 5 min", Pace.Roughly(TimeSpan.FromMinutes(65)));
        Assert.Equal("2 h", Pace.Roughly(TimeSpan.FromHours(2)));
    }

    /// <summary>Sixty an hour, measured over plenty of past running.</summary>
    private static readonly KnownPace Sixty = new(60d, TimeSpan.FromHours(2));

    private static double Minutes(TimeSpan? span) => span!.Value.TotalMinutes;

    [Fact]
    public void AKnownPaceAnswersBeforeTheRunHasOne()
    {
        // Nothing done, no time passed, but this ground is known: ten at sixty
        // an hour is ten minutes.
        Assert.Equal(TimeSpan.FromMinutes(10), Pace.TimeToGo(0, 10, TimeSpan.Zero, Sixty));
    }

    [Fact]
    public void ASlowStartDoesNotRunAwayWithIt()
    {
        // Two minutes in with nothing to show. Taken on its own that is a rate
        // of zero and no estimate at all; taken against what this ground has
        // always given, it is a couple of unlucky minutes.
        Assert.InRange(Minutes(Pace.TimeToGo(0, 10, TimeSpan.FromMinutes(2), Sixty)), 10d, 13d);
    }

    [Fact]
    public void AFastStartDoesNotRunAwayEither()
    {
        // Six in three minutes is a hundred and twenty an hour, which would
        // promise the rest in two minutes. Three minutes is not enough running
        // to believe that of a field known to give sixty.
        var blended = Minutes(Pace.TimeToGo(6, 10, TimeSpan.FromMinutes(3), Sixty));
        var alone = Minutes(Pace.TimeToGo(6, 10, TimeSpan.FromMinutes(3)));

        Assert.InRange(blended, 3d, 5d);
        Assert.True(blended > alone);
    }

    [Fact]
    public void SustainedSlownessIsBelievedInTheEnd()
    {
        // Half an hour for two is not bad luck any more, and the estimate
        // should have moved a long way from what the ground used to give.
        Assert.InRange(Minutes(Pace.TimeToGo(2, 10, TimeSpan.FromMinutes(30), Sixty)), 18d, 26d);
    }

    [Fact]
    public void ThinHistoryCountsForLittle()
    {
        // A pace measured over two minutes of farming is barely evidence, so
        // the run in hand should outweigh it quickly. The same run against a
        // well measured pace stays closer to it.
        var thin = new KnownPace(60d, TimeSpan.FromMinutes(2));

        var againstThin = Minutes(Pace.TimeToGo(1, 10, TimeSpan.FromMinutes(10), thin));
        var againstSolid = Minutes(Pace.TimeToGo(1, 10, TimeSpan.FromMinutes(10), Sixty));

        Assert.True(againstThin > againstSolid);
    }

    [Fact]
    public void AKnownPaceOfNothingIsNoPace()
    {
        // This ground is known and known to drop none of it. That says nothing
        // about how long the rest takes, so it falls back to the run in hand.
        Assert.Null(Pace.TimeToGo(0, 10, TimeSpan.FromMinutes(2), new KnownPace(0d, TimeSpan.FromHours(1))));
    }

    [Fact]
    public void AMetTargetIsStillDoneWithAKnownPace()
    {
        Assert.Equal(TimeSpan.Zero, Pace.TimeToGo(10, 10, TimeSpan.FromMinutes(5), Sixty));
    }

    [Fact]
    public void RoughlyRoundsRatherThanTruncates()
    {
        // 95 seconds is two minutes to anyone speaking, not one.
        Assert.Equal("2 min", Pace.Roughly(TimeSpan.FromSeconds(95)));
    }
}
