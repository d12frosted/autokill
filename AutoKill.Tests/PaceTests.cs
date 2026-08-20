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

    [Fact]
    public void RoughlyRoundsRatherThanTruncates()
    {
        // 95 seconds is two minutes to anyone speaking, not one.
        Assert.Equal("2 min", Pace.Roughly(TimeSpan.FromSeconds(95)));
    }
}
