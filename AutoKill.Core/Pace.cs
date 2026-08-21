namespace AutoKill.Core;

/// <summary>
/// A rate already measured, and how much running went into measuring it.
/// </summary>
/// <remarks>
/// The second half is what keeps it honest. A rate from two minutes of farming
/// and a rate from two hours are both "so many an hour", and treating them
/// alike would let one lucky trip speak as loudly as a season of them.
/// </remarks>
public readonly record struct KnownPace(double PerHour, TimeSpan From);

/// <summary>
/// How fast something accumulated, said per hour.
/// </summary>
/// <remarks>
/// Per hour because that is how farming is talked about, whatever the run's
/// actual length. A stretch under a minute gives no rate at all: one kill in
/// twenty seconds reads as 180 an hour, which no run ever delivers, and a
/// number that wrong is worse than none.
/// </remarks>
public static class Pace
{
    private static readonly TimeSpan Enough = TimeSpan.FromMinutes(1);

    // How much running a known pace is worth when the run in hand disagrees
    // with it. Long enough that the opening minutes, where one drop swings the
    // rate from nothing to double, cannot run away with the estimate; short
    // enough that a field genuinely poorer today is believed once it has spent
    // a while proving it.
    private static readonly TimeSpan KnownWorth = TimeSpan.FromMinutes(15);

    public static double? PerHour(int count, TimeSpan elapsed) =>
        elapsed < Enough ? null : count / elapsed.TotalHours;

    /// <summary>
    /// How much longer the rest should take at the pace shown so far, nothing
    /// when there is no pace to go on, and zero when there is no rest.
    /// </summary>
    /// <remarks>
    /// With a pace already known for this ground, the two are blended rather
    /// than swapped: the known one stands in for a stretch of running that the
    /// run in hand has to outweigh before it is believed. Swapping instead is
    /// what makes an estimate jump about, since a run only two minutes old has
    /// a rate that doubles or halves on every single drop, and an answer that
    /// moves like that is not worth reading.
    ///
    /// The known pace is only ever worth as much running as actually went into
    /// it, so a rate from one short trip gives way quickly and one from hours
    /// of farming holds its ground.
    /// </remarks>
    public static TimeSpan? TimeToGo(int done, int target, TimeSpan elapsed, KnownPace? known = null)
    {
        if (done >= target)
            return TimeSpan.Zero;

        var left = target - done;

        if (known is { PerHour: > 0 } pace)
        {
            var worth = pace.From < KnownWorth ? pace.From : KnownWorth;
            var hours = worth.TotalHours + elapsed.TotalHours;
            var rate = hours > 0 ? ((pace.PerHour * worth.TotalHours) + done) / hours : 0d;

            if (rate > 0)
                return TimeSpan.FromHours(left / rate);
        }

        if (done <= 0 || elapsed < Enough)
            return null;

        return elapsed * left / done;
    }

    /// <summary>
    /// A duration the way a person would say it: "14 min", "1 h 5 min". The
    /// precision of hh:mm:ss promises more than an estimate knows.
    /// </summary>
    public static string Roughly(TimeSpan span)
    {
        // Judged before rounding: forty seconds is under a minute, not "1 min".
        if (span < Enough)
            return "under a minute";

        var minutes = (int)Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero);
        if (minutes < 60)
            return $"{minutes} min";

        return minutes % 60 == 0
            ? $"{minutes / 60} h"
            : $"{minutes / 60} h {minutes % 60} min";
    }
}
