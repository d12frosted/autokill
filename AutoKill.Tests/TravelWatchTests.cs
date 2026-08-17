using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class TravelWatchTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    private static TravelWatch NewWatch() => new(Patience, Cooldown);

    private static DateTime At(double seconds) => Start + TimeSpan.FromSeconds(seconds);

    /// <summary>A watch that has already given up on spot 0, thirty seconds in.</summary>
    private static TravelWatch AlreadyGivenUp()
    {
        var watch = NewWatch();
        watch.Watch(0, 200f, At(0));
        watch.Watch(0, 200f, At(15));
        watch.Watch(0, 200f, At(30));
        return watch;
    }

    [Fact]
    public void ClosingOnTheSpotIsProgress()
    {
        var watch = NewWatch();
        for (var second = 0; second <= 120; second += 3)
            Assert.False(watch.Watch(0, 400f - (second * 2), At(second)).Stalled);
    }

    [Fact]
    public void AStandstillAsksForAFreshRouteBeforeAnythingElse()
    {
        var watch = NewWatch();
        watch.Watch(0, 200f, At(0));

        var again = watch.Watch(0, 200f, At(15));
        Assert.True(again.Stalled);
        Assert.False(again.GiveUp);
        Assert.False(watch.GivenUpOn(0, At(15)));
    }

    [Fact]
    public void StillNothingAfterThatGivesUpOnTheSpot()
    {
        var watch = NewWatch();
        watch.Watch(0, 200f, At(0));
        watch.Watch(0, 200f, At(15));

        var over = watch.Watch(0, 200f, At(30));
        Assert.True(over.GiveUp);
        Assert.True(watch.GivenUpOn(0, At(30)));
    }

    [Fact]
    public void DriftIsNotProgress()
    {
        // Standing still on a mount is not perfectly still, and a route that
        // ends short leaves the character hovering.
        var watch = NewWatch();
        watch.Watch(0, 200f, At(0));
        watch.Watch(0, 199.7f, At(7));

        Assert.True(watch.Watch(0, 199.8f, At(15)).Stalled);
    }

    [Fact]
    public void ADifferentSpotStartsTheClockAgain()
    {
        var watch = NewWatch();
        watch.Watch(0, 200f, At(0));
        watch.Watch(1, 200f, At(15));

        Assert.False(watch.Watch(1, 200f, At(25)).Stalled);
    }

    [Fact]
    public void SettingOffAgainStartsTheClockAgain()
    {
        // The same spot is travelled to over and over round a circuit, and a
        // clock left running between legs would trip on the first sample of the
        // next one.
        var watch = NewWatch();
        watch.Watch(0, 200f, At(0));
        watch.Watch(0, 200f, At(10));

        watch.SetOff();
        Assert.False(watch.Watch(0, 200f, At(20)).Stalled);
    }

    [Fact]
    public void OneSpotGetsOneSecondChance()
    {
        // Re-arming on any progress at all would let a route that crawls a few
        // yalms and stops keep the run travelling for good.
        var watch = NewWatch();
        watch.Watch(0, 200f, At(0));
        Assert.False(watch.Watch(0, 200f, At(15)).GiveUp);
        Assert.False(watch.Watch(0, 150f, At(20)).Stalled);
        Assert.True(watch.Watch(0, 150f, At(35)).GiveUp);
    }

    [Fact]
    public void SomewhereNeverTravelledToIsNotGivenUpOn()
    {
        Assert.False(NewWatch().GivenUpOn(0, At(0)));
    }

    [Fact]
    public void GivingUpOnOneSaysNothingAboutTheRest()
    {
        Assert.False(AlreadyGivenUp().GivenUpOn(1, At(30)));
    }

    [Fact]
    public void OneGivenUpOnIsWorthTryingAgainEventually()
    {
        // A mesh that was still loading, or an approach from somewhere else, can
        // turn a spot that would not path into one that will.
        var watch = AlreadyGivenUp();
        Assert.True(watch.GivenUpOn(0, At(300)));
        Assert.False(watch.GivenUpOn(0, At(330)));
    }
}
