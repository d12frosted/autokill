using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class SpotWatchTests
{
    private static readonly DateTime Noon = new(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);

    private static DateTime After(double seconds) => Noon.AddSeconds(seconds);

    [Fact]
    public void ArrivingAtAnEmptySpotMeasuresNothing()
    {
        var watch = new SpotWatch();

        // It may have been standing empty for an hour before we got here.
        watch.Empty(Noon);

        Assert.Null(watch.Occupied(After(30)));
    }

    [Fact]
    public void WhatWeClearedComingBackIsMeasured()
    {
        var watch = new SpotWatch();

        watch.Occupied(Noon);
        watch.Empty(After(10));

        Assert.Equal(TimeSpan.FromSeconds(80), watch.Occupied(After(90)));
    }

    /// <summary>
    /// The clock starts when the spot first read empty, not when the last of a
    /// run of empty ticks did. A tick is a moment, and there are a lot of them.
    /// </summary>
    [Fact]
    public void TheClockStartsAtTheFirstEmptyTick()
    {
        var watch = new SpotWatch();

        watch.Occupied(Noon);
        watch.Empty(After(10));
        watch.Empty(After(11));
        watch.Empty(After(12));

        Assert.Equal(TimeSpan.FromSeconds(80), watch.Occupied(After(90)));
    }

    [Fact]
    public void StillStandingThereMeasuresNothing()
    {
        var watch = new SpotWatch();

        watch.Occupied(Noon);

        Assert.Null(watch.Occupied(After(30)));
    }

    /// <summary>
    /// Moving on ends the watch. Whatever happens at a spot nobody is looking
    /// at is not something we saw.
    /// </summary>
    [Fact]
    public void LeavingEndsTheWatch()
    {
        var watch = new SpotWatch();

        watch.Occupied(Noon);
        watch.Empty(After(10));
        watch.Left();

        Assert.Null(watch.Occupied(After(90)));
    }

    [Fact]
    public void ASecondEmptyingIsMeasuredToo()
    {
        var watch = new SpotWatch();

        watch.Occupied(Noon);
        watch.Empty(After(10));
        watch.Occupied(After(90));
        watch.Empty(After(100));

        Assert.Equal(TimeSpan.FromSeconds(60), watch.Occupied(After(160)));
    }
}
