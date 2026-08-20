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
}
