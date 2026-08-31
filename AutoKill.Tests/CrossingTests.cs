using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class CrossingTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(10);

    private static Crossing NewCrossing(params uint[] gates) => new(gates, Patience, Retry);

    private static DateTime At(double seconds) => Start + TimeSpan.FromSeconds(seconds);

    [Fact]
    public void TakesTheFirstGateTheMomentItIsFree()
    {
        var crossing = NewCrossing(92, 91);

        Assert.Equal(CrossingStep.Go, crossing.Check(busy: false, At(0)));
        Assert.Equal(92u, crossing.Gate);
    }

    [Fact]
    public void WaitsOutABusyCharacter()
    {
        // Landing at the aetheryte is a loading screen, and the aethernet menu
        // is not there to be opened until it has finished.
        var crossing = NewCrossing(92, 91);

        Assert.Equal(CrossingStep.Wait, crossing.Check(busy: true, At(0)));
        Assert.Equal(CrossingStep.Wait, crossing.Check(busy: true, At(3)));
        Assert.Equal(CrossingStep.Go, crossing.Check(busy: false, At(5)));
    }

    [Fact]
    public void DoesNotAskAgainWhileTheLastAskIsStillGoing()
    {
        // An aethernet hop is a menu, a click and a load. Asking again on the
        // next tick would be asking over the top of the one already running.
        var crossing = NewCrossing(92, 91);
        crossing.Check(busy: false, At(0));

        Assert.Equal(CrossingStep.Wait, crossing.Check(busy: false, At(4)));
        Assert.Equal(CrossingStep.Go, crossing.Check(busy: false, At(11)));
    }

    [Fact]
    public void TriesTheOtherGateWhenTheFirstOneGoesNowhere()
    {
        // Nothing tells us why a gate did nothing, and the likeliest reason is
        // that it was never attuned. The other one lands in the same zone.
        var crossing = NewCrossing(92, 91);

        Assert.Equal(CrossingStep.Go, crossing.Check(busy: false, At(0)));
        Assert.Equal(92u, crossing.Gate);

        Assert.Equal(CrossingStep.Go, crossing.Check(busy: false, At(11)));
        Assert.Equal(91u, crossing.Gate);
    }

    [Fact]
    public void ComesBackRoundToTheFirstGate()
    {
        // A hop eaten by a loading screen deserves a second go rather than
        // spending the rest of the patience on gates already ruled out.
        var crossing = NewCrossing(92, 91);
        crossing.Check(busy: false, At(0));
        crossing.Check(busy: false, At(11));

        Assert.Equal(CrossingStep.Go, crossing.Check(busy: false, At(22)));
        Assert.Equal(92u, crossing.Gate);
    }

    [Fact]
    public void OneGateIsTriedAgainRatherThanOnlyOnce()
    {
        var crossing = NewCrossing(92);

        Assert.Equal(CrossingStep.Go, crossing.Check(busy: false, At(0)));
        Assert.Equal(CrossingStep.Go, crossing.Check(busy: false, At(11)));
        Assert.Equal(92u, crossing.Gate);
    }

    [Fact]
    public void GivesUpEventually()
    {
        // Stuck busy, or hops that keep landing nowhere. A character left
        // standing in Idyllshire is a run that should say so and stop.
        var crossing = NewCrossing(92, 91);
        for (var second = 0; second < 45; second += 5)
            Assert.Equal(CrossingStep.Wait, crossing.Check(busy: true, At(second)));

        Assert.Equal(CrossingStep.GiveUp, crossing.Check(busy: true, At(46)));
    }

    [Fact]
    public void GivingUpBeatsGoing()
    {
        var crossing = NewCrossing(92, 91);
        crossing.Check(busy: true, At(0));

        Assert.Equal(CrossingStep.GiveUp, crossing.Check(busy: false, At(46)));
    }

    [Fact]
    public void PatienceRunsFromTheFirstLook()
    {
        // Built when the teleport was cast, consulted only once the character
        // has landed. The waiting starts where the looking does.
        var crossing = NewCrossing(92, 91);

        Assert.Equal(CrossingStep.Wait, crossing.Check(busy: true, At(100)));
        Assert.Equal(CrossingStep.Wait, crossing.Check(busy: true, At(144)));
        Assert.Equal(CrossingStep.GiveUp, crossing.Check(busy: true, At(146)));
    }

    [Fact]
    public void GivesUpWhenThereIsNoGate()
    {
        // Nothing to try is not something patience will fix.
        Assert.Equal(CrossingStep.GiveUp, NewCrossing().Check(busy: false, At(0)));
    }
}
