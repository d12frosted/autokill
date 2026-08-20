using System.Numerics;
using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class FieldWatchTests
{
    private static readonly DateTime Noon = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    private const uint Rroneek = 1;
    private const uint Lobo = 2;

    private static readonly Vector3 Standing = Vector3.Zero;
    private static readonly Vector3 Home = new(10, 0, 10);
    private static readonly Vector3 NextSpot = new(300, 0, 0);

    private static FieldMob Mob(ulong id, Vector3 where, uint what = Rroneek) =>
        new(id, what, where);

    /// <summary>
    /// Whatever is standing here when we arrive may have been standing here for
    /// an hour. Only what we watched go down is timed.
    /// </summary>
    [Fact]
    public void WhatIsAlreadyStandingThereMeasuresNothing()
    {
        var field = new Field();

        Assert.Empty(field.At(0, Mob(1, Home)));
        Assert.Empty(field.At(1, Mob(1, Home)));
    }

    [Fact]
    public void OneDyingAndOneStandingThereAgainIsMeasured()
    {
        var field = new Field();

        field.At(0, Mob(1, Home));
        field.At(5);

        var measured = Assert.Single(field.At(45, Mob(2, Home)));

        Assert.Equal(Rroneek, measured.NameId);
        Assert.Equal(TimeSpan.FromSeconds(40), measured.Took);
    }

    /// <summary>The gap is timed from the death, and closed exactly once.</summary>
    [Fact]
    public void ASpawnPointIsOnlyMeasuredOnce()
    {
        var field = new Field();

        field.At(0, Mob(1, Home));
        field.At(5);
        field.At(45, Mob(2, Home));

        Assert.Empty(field.At(46, Mob(2, Home)));
    }

    /// <summary>
    /// A ranged run kills things where it stands, not where they live, so a
    /// death yalms from home says nothing about that spawn point. Timing runs
    /// from where it was first seen.
    /// </summary>
    [Fact]
    public void ItIsTimedFromWhereItLivedNotFromWhereItDied()
    {
        var field = new Field();

        field.At(0, Mob(1, Home));

        // Pulled, so it walks all the way in and dies at our feet.
        field.At(5, Mob(1, new Vector3(2, 0, 2)));
        field.At(10);

        var measured = Assert.Single(field.At(50, Mob(2, Home)));
        Assert.Equal(TimeSpan.FromSeconds(40), measured.Took);
    }

    [Fact]
    public void SomethingElseEntirelyIsNotItComingBack()
    {
        var field = new Field();

        field.At(0, Mob(1, Home));
        field.At(5);

        Assert.Empty(field.At(45, Mob(2, Home, Lobo)));
    }

    [Fact]
    public void SomethingStandingUpElsewhereIsNotThisSpawnPoint()
    {
        var field = new Field(new FieldWatch(samePlace: 10f));

        field.At(0, Mob(1, Home));
        field.At(5);

        Assert.Empty(field.At(45, Mob(2, new Vector3(40, 0, 40))));
    }

    /// <summary>
    /// Things drop out of the object table at a distance, and one doing that is
    /// not one that died. Only what went down close enough to watch is timed.
    /// </summary>
    [Fact]
    public void SomethingVanishingFarOffIsNotADeath()
    {
        var field = new Field(new FieldWatch(watching: 50f));
        var away = new Vector3(200, 0, 0);

        field.At(0, Mob(1, away));
        field.At(5);

        Assert.Empty(field.At(45, Mob(2, away)));
    }

    /// <summary>
    /// A spawn point left behind comes back where nobody is looking, so the
    /// wait would run from a death we saw to an arrival we did not.
    /// </summary>
    [Fact]
    public void WalkingAwayGivesUpOnWhatWasLeftBehind()
    {
        var field = new Field(new FieldWatch(watching: 50f));

        field.At(0, Mob(1, Home));
        field.At(5);

        // Off to the next spot, and back again.
        field.From(20, NextSpot);

        Assert.Empty(field.From(60, Standing, Mob(2, Home)));
    }

    [Fact]
    public void LeavingEndsTheWatch()
    {
        var watch = new FieldWatch();
        var field = new Field(watch);

        field.At(0, Mob(1, Home));
        field.At(5);
        watch.Left();

        Assert.Empty(field.At(45, Mob(2, Home)));
    }

    /// <summary>
    /// A gap in the looking is a gap in the watching: a loading screen, a run
    /// stopped and started again, a zone left and come back to. Anything could
    /// have happened in it, so nothing measured across it is trusted.
    /// </summary>
    [Fact]
    public void ALookAfterALongSilenceStartsOver()
    {
        var watch = new FieldWatch(blind: TimeSpan.FromSeconds(15));

        watch.Look(Noon, Standing, [Mob(1, Home)]);
        watch.Look(Noon.AddSeconds(5), Standing, []);

        Assert.Empty(watch.Look(Noon.AddSeconds(45), Standing, [Mob(2, Home)]));
    }

    [Fact]
    public void ADeathWaitedOnTooLongIsForgotten()
    {
        var field = new Field(new FieldWatch(remembered: TimeSpan.FromMinutes(10)));

        field.At(0, Mob(1, Home));
        field.At(5);

        Assert.Empty(field.At(700, Mob(2, Home)));
    }

    /// <summary>
    /// The same creature dropping out of the table and coming back is the same
    /// creature. Nothing respawned, so nothing is timed.
    /// </summary>
    [Fact]
    public void TheSameOneComingBackIsNotARespawn()
    {
        var field = new Field();

        field.At(0, Mob(1, Home));
        field.At(5);

        Assert.Empty(field.At(10, Mob(1, Home)));

        // And having been written off, it is not held against the next one.
        Assert.Empty(field.At(15, Mob(1, Home)));
    }

    /// <summary>
    /// A field is cleared in a handful of seconds and comes back the same way.
    /// Each spawn point answers the death nearest it rather than whichever one
    /// happened to come first.
    /// </summary>
    [Fact]
    public void AClearedFieldIsMeasuredSpawnPointBySpawnPoint()
    {
        var field = new Field(new FieldWatch(samePlace: 10f));

        var near = new Vector3(5, 0, 0);
        var far = new Vector3(30, 0, 0);

        field.At(0, Mob(1, near), Mob(2, far));

        // The far one goes down first, the near one ten seconds later.
        field.At(10, Mob(1, near));
        field.At(20);

        var back = field.At(60, Mob(3, near), Mob(4, far));

        Assert.Equal(2, back.Count);
        Assert.Equal(TimeSpan.FromSeconds(40), back.Single(r => r.Id == 3).Took);
        Assert.Equal(TimeSpan.FromSeconds(50), back.Single(r => r.Id == 4).Took);
    }

    /// <summary>
    /// Two spawn points close enough to be confused are still both spawn
    /// points: the pairing may be the wrong way round, the pair of gaps is not.
    /// </summary>
    [Fact]
    public void TwoDeathsAtOnePlaceAreBothAnsweredFor()
    {
        var field = new Field(new FieldWatch(samePlace: 10f));

        var here = new Vector3(20, 0, 0);
        var beside = new Vector3(23, 0, 0);

        field.At(0, Mob(1, here), Mob(2, beside));
        field.At(5);

        var back = field.At(45, Mob(3, here), Mob(4, beside));

        Assert.Equal(2, back.Count);
        Assert.All(back, respawn => Assert.Equal(TimeSpan.FromSeconds(40), respawn.Took));
    }

    /// <summary>
    /// The case the whole thing exists for: six spawn points, something killed
    /// every few seconds, and never a moment when the field is empty or left.
    /// Neither spot measurement can close in a field like this, and it is the
    /// kind farmed hardest.
    /// </summary>
    [Fact]
    public void AFieldThatIsNeverEmptyStillLearnsWhatItIsDoing()
    {
        const double respawn = 45;

        var watch = new FieldWatch();
        var points = Enumerable.Range(0, 6).Select(i => new Vector3(i * 15, 0, 10)).ToList();

        // Each point holds whatever is standing on it, or the second it comes
        // back on.
        var standing = points.Select((_, i) => (ulong)(i + 1)).ToList();
        var due = points.Select(_ => 0.0).ToList();
        var next = (ulong)points.Count + 1;

        var measured = new List<double>();

        for (var second = 0; second <= 600; second++)
        {
            // One kill every five seconds, which is a slow rotation and a fast
            // enough field that something is always up.
            if (second > 0 && second % 5 == 0)
            {
                var alive = standing.FindIndex(id => id != 0);
                if (alive >= 0)
                {
                    standing[alive] = 0;
                    due[alive] = second + respawn;
                }
            }

            for (var point = 0; point < points.Count; point++)
            {
                if (standing[point] == 0 && due[point] <= second)
                    standing[point] = next++;
            }

            var up = points
                .Select((where, i) => (Where: where, Id: standing[i]))
                .Where(point => point.Id != 0)
                .Select(point => Mob(point.Id, point.Where))
                .ToList();

            measured.AddRange(watch
                .Look(Noon.AddSeconds(second), Standing, up)
                .Select(back => back.Took.TotalSeconds));
        }

        // Ten minutes of it, so this is not a trickle.
        Assert.True(measured.Count > 20, $"only {measured.Count} measured");

        var expect = Repopulation.From(measured, []);
        Assert.NotNull(expect);
        Assert.True(expect!.Value.Timed);
        Assert.Equal(respawn, expect.Value.Typical.TotalSeconds, 1);
    }

    /// <summary>
    /// The plugin looks about once a second and every rule in the watch rests
    /// on that, so the tests look that often too. Skipping the quiet seconds in
    /// a test would be telling the watch nobody was there.
    /// </summary>
    private sealed class Field(FieldWatch? watch = null)
    {
        private readonly FieldWatch watch = watch ?? new FieldWatch();

        private double when;
        private Vector3 from = Standing;
        private IReadOnlyList<FieldMob> standing = [];

        /// <summary>Look from where we already were.</summary>
        public IReadOnlyList<Respawn> At(double seconds, params FieldMob[] mobs) =>
            From(seconds, from, mobs);

        /// <summary>Look from somewhere else, having walked there.</summary>
        public IReadOnlyList<Respawn> From(double seconds, Vector3 where, params FieldMob[] mobs)
        {
            for (var tick = when + 1; tick < seconds; tick++)
                watch.Look(Noon.AddSeconds(tick), from, standing);

            when = seconds;
            from = where;
            standing = mobs;

            return watch.Look(Noon.AddSeconds(seconds), where, mobs);
        }
    }
}
