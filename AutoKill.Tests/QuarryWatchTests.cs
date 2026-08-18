using System.Numerics;
using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class QuarryWatchTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(90);

    private const ulong Mob = 42;
    private const ulong Other = 7;

    private static QuarryWatch NewWatch() => new(Patience, Cooldown, wandered: 8f);

    private static DateTime At(double seconds) => Start + TimeSpan.FromSeconds(seconds);

    /// <summary>A watch that has already given up on <see cref="Mob"/>, at the origin, ten seconds in.</summary>
    private static QuarryWatch AlreadyGivenUp()
    {
        var watch = NewWatch();
        watch.Watch(Mob, Vector3.Zero, 40f, 1000, inRange: false, At(0));
        watch.Watch(Mob, Vector3.Zero, 40f, 1000, inRange: false, At(10));
        watch.Watch(Mob, Vector3.Zero, 40f, 1000, inRange: false, At(20));
        return watch;
    }

    [Fact]
    public void ClosingTheDistanceIsProgress()
    {
        var watch = NewWatch();
        for (var second = 0; second <= 60; second += 2)
        {
            var check = watch.Watch(Mob, Vector3.Zero, 100f - second, 1000, inRange: false, At(second));
            Assert.False(check.Stalled);
        }
    }

    [Fact]
    public void HealthComingDownIsProgressEvenStandingStill()
    {
        // A long fight against something tanky is not a stall. Nothing moves for
        // minutes and that is exactly what killing it looks like.
        var watch = NewWatch();
        uint hp = 1000;
        for (var second = 0; second <= 60; second += 3)
        {
            var check = watch.Watch(Mob, Vector3.Zero, 3f, hp, inRange: true, At(second));
            Assert.False(check.Stalled);
            hp -= 20;
        }
    }

    [Fact]
    public void ADistanceThatNeverClosesIsGivenUpOn()
    {
        var watch = NewWatch();
        watch.Watch(Mob, Vector3.Zero, 40f, 1000, inRange: false, At(0));

        // The first stall asks for something else to be tried rather than
        // abandoning it outright.
        var nudge = watch.Watch(Mob, Vector3.Zero, 40f, 1000, inRange: false, At(10));
        Assert.Equal(QuarryTrouble.OutOfReach, nudge.Trouble);
        Assert.False(nudge.GiveUp);

        var over = watch.Watch(Mob, Vector3.Zero, 40f, 1000, inRange: false, At(20));
        Assert.Equal(QuarryTrouble.OutOfReach, over.Trouble);
        Assert.True(over.GiveUp);
        Assert.True(watch.GivenUpOn(Mob, Vector3.Zero, At(20)));
    }

    [Fact]
    public void StandingInRangeWithNothingLandingReadsAsSight()
    {
        var watch = NewWatch();
        watch.Watch(Mob, Vector3.Zero, 3f, 1000, inRange: true, At(0));
        watch.Watch(Mob, Vector3.Zero, 3f, 1000, inRange: true, At(10));

        var over = watch.Watch(Mob, Vector3.Zero, 3f, 1000, inRange: true, At(20));
        Assert.Equal(QuarryTrouble.OutOfSight, over.Trouble);
        Assert.True(over.GiveUp);
    }

    [Fact]
    public void StandingInRangeBehindSomethingIsNotWorthWaitingOn()
    {
        // Something solid between the two is visible at once, and ten seconds
        // of nothing landing would only say the same thing.
        var watch = NewWatch();
        watch.Watch(Mob, Vector3.Zero, 18f, 1000, inRange: true, At(0));

        var nudge = watch.Watch(Mob, Vector3.Zero, 18f, 1000, inRange: true, At(1), inSight: false);
        Assert.Equal(QuarryTrouble.Blocked, nudge.Trouble);
        Assert.False(nudge.GiveUp);
    }

    [Fact]
    public void BeingBlockedDoesNotSpendTheSecondTry()
    {
        // The answer to being blocked is to walk in until it can be seen, and
        // the place it stops may be wrong about that. Nothing landing from
        // there is the ordinary stall and gets the ordinary second try.
        var watch = NewWatch();
        watch.Watch(Mob, Vector3.Zero, 18f, 1000, inRange: true, At(0));
        watch.Watch(Mob, Vector3.Zero, 18f, 1000, inRange: true, At(1), inSight: false);

        // Walking in is progress, and progress resets the clock.
        Assert.False(watch.Watch(Mob, Vector3.Zero, 15f, 1000, inRange: true, At(4)).Stalled);

        var nudge = watch.Watch(Mob, Vector3.Zero, 15f, 1000, inRange: true, At(14));
        Assert.Equal(QuarryTrouble.OutOfSight, nudge.Trouble);
        Assert.False(nudge.GiveUp);

        var over = watch.Watch(Mob, Vector3.Zero, 15f, 1000, inRange: true, At(24));
        Assert.True(over.GiveUp);
    }

    [Fact]
    public void BlockedOutOfRangeIsNothingYet()
    {
        // Far off, the way there decides what is between the two, not a line
        // drawn from here.
        var watch = NewWatch();
        watch.Watch(Mob, Vector3.Zero, 40f, 1000, inRange: false, At(0));

        Assert.False(watch.Watch(Mob, Vector3.Zero, 39f, 1000, inRange: false, At(1), inSight: false).Stalled);
    }

    [Fact]
    public void BlockedButHurtingIsAFightNotAStall()
    {
        // If its health is coming down then something is reaching it, whatever
        // a line drawn between the two says.
        var watch = NewWatch();
        watch.Watch(Mob, Vector3.Zero, 18f, 1000, inRange: true, At(0));

        Assert.False(watch.Watch(Mob, Vector3.Zero, 18f, 900, inRange: true, At(1), inSight: false).Stalled);
    }

    [Fact]
    public void BlockedIsOnlyAnsweredOnce()
    {
        // Saying it again every tick would keep the run walking in for as long
        // as the line stayed blocked. Once is the nudge; after that the
        // ordinary clock decides.
        var watch = NewWatch();
        watch.Watch(Mob, Vector3.Zero, 18f, 1000, inRange: true, At(0));
        watch.Watch(Mob, Vector3.Zero, 18f, 1000, inRange: true, At(1), inSight: false);

        Assert.False(watch.Watch(Mob, Vector3.Zero, 18f, 1000, inRange: true, At(2), inSight: false).Stalled);
        Assert.False(watch.Watch(Mob, Vector3.Zero, 18f, 1000, inRange: true, At(8), inSight: false).Stalled);

        var nudge = watch.Watch(Mob, Vector3.Zero, 18f, 1000, inRange: true, At(11), inSight: false);
        Assert.Equal(QuarryTrouble.OutOfSight, nudge.Trouble);
        Assert.False(nudge.GiveUp);
        Assert.True(watch.Watch(Mob, Vector3.Zero, 18f, 1000, inRange: true, At(21), inSight: false).GiveUp);
    }

    [Fact]
    public void ANewQuarryCanBeBlockedAgain()
    {
        var watch = NewWatch();
        watch.Watch(Mob, Vector3.Zero, 18f, 1000, inRange: true, At(0));
        watch.Watch(Mob, Vector3.Zero, 18f, 1000, inRange: true, At(1), inSight: false);
        watch.Watch(Other, Vector3.Zero, 18f, 1000, inRange: true, At(2));

        Assert.Equal(QuarryTrouble.Blocked, watch.Watch(Other, Vector3.Zero, 18f, 1000, inRange: true, At(3), inSight: false).Trouble);
    }

    [Fact]
    public void HavingArrivedOnceTheTroubleIsNotGettingThere()
    {
        // It was reached, so the walk is not what failed. A mob that shuffles a
        // few yalms off mid fight does not turn it back into a travel problem.
        var watch = NewWatch();
        watch.Watch(Mob, Vector3.Zero, 3f, 1000, inRange: true, At(0));
        watch.Watch(Mob, Vector3.Zero, 8f, 1000, inRange: false, At(10));

        var over = watch.Watch(Mob, Vector3.Zero, 8f, 1000, inRange: false, At(20));
        Assert.Equal(QuarryTrouble.OutOfSight, over.Trouble);
    }

    [Fact]
    public void ShufflingOnTheSpotIsNotProgress()
    {
        // A character holding position drifts, and drift is not getting closer.
        var watch = NewWatch();
        watch.Watch(Mob, Vector3.Zero, 40f, 1000, inRange: false, At(0));
        watch.Watch(Mob, Vector3.Zero, 39.8f, 1000, inRange: false, At(5));
        watch.Watch(Mob, Vector3.Zero, 39.9f, 1000, inRange: false, At(8));

        Assert.True(watch.Watch(Mob, Vector3.Zero, 39.7f, 1000, inRange: false, At(10)).Stalled);
    }

    [Fact]
    public void HealingBackUpIsNotProgress()
    {
        // Dropping combat and regenerating is the clearest sign yet that nothing
        // is reaching it.
        var watch = NewWatch();
        watch.Watch(Mob, Vector3.Zero, 3f, 500, inRange: true, At(0));
        watch.Watch(Mob, Vector3.Zero, 3f, 900, inRange: true, At(5));

        Assert.True(watch.Watch(Mob, Vector3.Zero, 3f, 1000, inRange: true, At(10)).Stalled);
    }

    [Fact]
    public void AFreshQuarryStartsTheClockAgain()
    {
        var watch = NewWatch();
        watch.Watch(Mob, Vector3.Zero, 40f, 1000, inRange: false, At(0));
        watch.Watch(Other, Vector3.Zero, 40f, 1000, inRange: false, At(10));

        Assert.False(watch.Watch(Other, Vector3.Zero, 40f, 1000, inRange: false, At(15)).Stalled);
    }

    [Fact]
    public void OneQuarryGetsOneSecondChance()
    {
        // Re-arming on every scrap of progress would let something that inches
        // forward and stops again hold the run there for good.
        var watch = NewWatch();
        watch.Watch(Mob, Vector3.Zero, 40f, 1000, inRange: false, At(0));
        Assert.False(watch.Watch(Mob, Vector3.Zero, 40f, 1000, inRange: false, At(10)).GiveUp);
        Assert.False(watch.Watch(Mob, Vector3.Zero, 30f, 1000, inRange: false, At(12)).Stalled);
        Assert.True(watch.Watch(Mob, Vector3.Zero, 30f, 1000, inRange: false, At(22)).GiveUp);
    }

    [Fact]
    public void NothingIsPassedOverUntilItHasBeenGivenUpOn()
    {
        var watch = NewWatch();
        watch.Watch(Mob, Vector3.Zero, 40f, 1000, inRange: false, At(0));

        var nudge = watch.Watch(Mob, Vector3.Zero, 40f, 1000, inRange: false, At(10));
        Assert.True(nudge.Stalled);
        Assert.False(nudge.GiveUp);
        Assert.False(watch.GivenUpOn(Mob, Vector3.Zero, At(10)));
    }

    [Fact]
    public void SomethingNeverSeenIsNotPassedOver()
    {
        Assert.False(NewWatch().GivenUpOn(Mob, Vector3.Zero, At(0)));
    }

    [Fact]
    public void OneGivenUpOnIsWorthTryingAgainEventually()
    {
        var watch = AlreadyGivenUp();
        Assert.True(watch.GivenUpOn(Mob, Vector3.Zero, At(100)));
        Assert.False(watch.GivenUpOn(Mob, Vector3.Zero, At(110)));
    }

    [Fact]
    public void OneThatHasWanderedOffIsWorthTryingAgainAtOnce()
    {
        // Whatever was in the way is unlikely to still be in the way once it has
        // walked out from behind it.
        var watch = AlreadyGivenUp();
        Assert.True(watch.GivenUpOn(Mob, new Vector3(5f, 0f, 0f), At(25)));
        Assert.False(watch.GivenUpOn(Mob, new Vector3(20f, 0f, 0f), At(25)));
    }

    [Fact]
    public void GivingUpOnOneSaysNothingAboutTheRest()
    {
        var watch = AlreadyGivenUp();
        Assert.False(watch.GivenUpOn(Other, Vector3.Zero, At(20)));
    }
}
