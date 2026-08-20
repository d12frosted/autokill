using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class HomecomingTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    private static Homecoming NewTrip() => new(Patience, Retry);

    private static DateTime At(double seconds) => Start + TimeSpan.FromSeconds(seconds);

    [Fact]
    public void GoesTheMomentItIsFree()
    {
        Assert.Equal(HomeStep.Go, NewTrip().Check(busy: false, At(0)));
    }

    [Fact]
    public void WaitsOutABusyCharacter()
    {
        // The last kill leaves combat trailing, and a teleport cast is refused
        // for as long as it lasts.
        var trip = NewTrip();
        Assert.Equal(HomeStep.Wait, trip.Check(busy: true, At(0)));
        Assert.Equal(HomeStep.Wait, trip.Check(busy: true, At(4)));
        Assert.Equal(HomeStep.Go, trip.Check(busy: false, At(8)));
    }

    [Fact]
    public void DoesNotSpamTheTeleport()
    {
        // A cast that was accepted takes a few seconds to leave, and asking
        // again mid-cast would cancel the one already going.
        var trip = NewTrip();
        trip.Check(busy: false, At(0));

        Assert.Equal(HomeStep.Wait, trip.Check(busy: false, At(2)));
        Assert.Equal(HomeStep.Go, trip.Check(busy: false, At(6)));
    }

    [Fact]
    public void GivesUpEventually()
    {
        // Stuck in combat, or a teleport refused over and over, should not
        // leave a trip pending forever after the run it belonged to.
        var trip = NewTrip();
        for (var second = 0; second < 30; second += 3)
            Assert.Equal(HomeStep.Wait, trip.Check(busy: true, At(second)));

        Assert.Equal(HomeStep.GiveUp, trip.Check(busy: true, At(31)));
    }

    [Fact]
    public void PatienceRunsFromTheFirstLook()
    {
        // A trip created long before it is first consulted has not been waiting.
        var trip = NewTrip();
        Assert.Equal(HomeStep.Wait, trip.Check(busy: true, At(100)));
        Assert.Equal(HomeStep.Wait, trip.Check(busy: true, At(129)));
        Assert.Equal(HomeStep.GiveUp, trip.Check(busy: true, At(131)));
    }

    [Fact]
    public void GivingUpBeatsGoing()
    {
        // Free at last, but far too late: the player has long since walked off,
        // and a surprise teleport half a minute later is worse than none.
        var trip = NewTrip();
        trip.Check(busy: true, At(0));

        Assert.Equal(HomeStep.GiveUp, trip.Check(busy: false, At(31)));
    }
}
