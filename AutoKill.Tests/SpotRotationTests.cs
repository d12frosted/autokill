using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class SpotRotationTests
{
    private static readonly TimeSpan Respawn = TimeSpan.FromSeconds(100);

    private static SpotState Spot(int index, int spawns, double? clearedSecondsAgo, float away = 0f) =>
        new(index, spawns, clearedSecondsAgo is { } s ? TimeSpan.FromSeconds(s) : null, away);

    [Fact]
    public void AnUnvisitedSpotComesFirst()
    {
        // Never cleared means never looked at, and a spot nobody has been to is
        // worth more than one emptied a moment ago however dense it is.
        SpotState[] spots = [Spot(0, 3, 1), Spot(1, 20, null)];
        Assert.Equal(1, SpotRotation.PickNext(spots, current: 0, Respawn, jitter: 0));
    }

    [Fact]
    public void TheSpotClearedLongestAgoWins()
    {
        SpotState[] spots = [Spot(0, 5, 10), Spot(1, 5, 90), Spot(2, 5, 40)];
        Assert.Equal(1, SpotRotation.PickNext(spots, current: 0, Respawn, jitter: 0));
    }

    [Fact]
    public void DensityBreaksTiesBetweenEquallyReadySpots()
    {
        SpotState[] spots = [Spot(0, 2, 100), Spot(1, 9, 100)];
        Assert.Equal(1, SpotRotation.PickNext(spots, current: 0, Respawn, jitter: 0));
    }

    [Fact]
    public void APackedSpotDoesNotBeatOneThatIsActuallyReady()
    {
        // Readiness is capped, so a big spot cleared seconds ago cannot outrank
        // a smaller one that has had time to repopulate.
        SpotState[] spots = [Spot(0, 30, 2), Spot(1, 4, 200)];
        Assert.Equal(1, SpotRotation.PickNext(spots, current: 0, Respawn, jitter: 0));
    }

    [Fact]
    public void PastFullRespawnExtraWaitingAddsNothing()
    {
        // Both are fully repopulated, so the denser one wins rather than the one
        // that has been sitting longest.
        SpotState[] spots = [Spot(0, 3, 5000), Spot(1, 12, 150)];
        Assert.Equal(1, SpotRotation.PickNext(spots, current: 0, Respawn, jitter: 0));
    }

    [Fact]
    public void TheCurrentSpotIsNotChosenAgain()
    {
        SpotState[] spots = [Spot(0, 50, 900), Spot(1, 1, 900)];
        Assert.Equal(1, SpotRotation.PickNext(spots, current: 0, Respawn, jitter: 0));
    }

    [Fact]
    public void ASingleSpotIsAlwaysTheAnswer()
    {
        SpotState[] spots = [Spot(0, 5, 1)];
        Assert.Equal(0, SpotRotation.PickNext(spots, current: 0, Respawn, jitter: 0));
    }

    [Fact]
    public void NoSpotsGivesTheCurrentOne()
    {
        Assert.Equal(3, SpotRotation.PickNext([], current: 3, Respawn, jitter: 0));
    }

    [Fact]
    public void TheNearerOfTwoEquallyReadySpotsWins()
    {
        // Twelve scattered spots mean the walking between them is most of the
        // run, so distance has to count for something.
        SpotState[] spots = [Spot(0, 5, 0), Spot(1, 5, 200, away: 400f), Spot(2, 5, 200, away: 60f)];
        Assert.Equal(2, SpotRotation.PickNext(spots, current: 0, Respawn, jitter: 0));
    }

    [Fact]
    public void DistanceDoesNotOverrulePlentyMoreMobs()
    {
        // Close but nearly empty should still lose to a full field a little
        // further off, or the circuit never leaves the nearest corner.
        SpotState[] spots = [Spot(0, 5, 0), Spot(1, 20, 200, away: 200f), Spot(2, 1, 200, away: 80f)];
        Assert.Equal(1, SpotRotation.PickNext(spots, current: 0, Respawn, jitter: 0));
    }

    [Fact]
    public void SomewhereUnvisitedIsStillWorthTheWalk()
    {
        SpotState[] spots = [Spot(0, 5, 0), Spot(1, 5, 300, away: 40f), Spot(2, 5, null, away: 350f)];
        Assert.Equal(2, SpotRotation.PickNext(spots, current: 0, Respawn, jitter: 0));
    }

    [Fact]
    public void JitterCanReorderNearEqualSpots()
    {
        // A run that always takes the same route round the same field looks
        // exactly like what it is, so close calls are allowed to go either way.
        SpotState[] spots = [Spot(0, 5, 0), Spot(1, 10, 100), Spot(2, 10, 101)];
        var picks = new HashSet<int>();
        for (var seed = 0; seed < 40; seed++)
            picks.Add(SpotRotation.PickNext(spots, current: 0, Respawn, jitter: 0.3, seed: seed));

        Assert.True(picks.Count > 1, "jitter never changed the choice");
        Assert.DoesNotContain(0, picks);
    }

    [Fact]
    public void JitterNeverPicksSomethingHopeless()
    {
        SpotState[] spots = [Spot(0, 5, 500), Spot(1, 8, 500), Spot(2, 1, 1)];
        for (var seed = 0; seed < 40; seed++)
            Assert.NotEqual(2, SpotRotation.PickNext(spots, current: 0, Respawn, jitter: 0.3, seed: seed));
    }
}
