namespace AutoKill.Core;

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

    public static double? PerHour(int count, TimeSpan elapsed) =>
        elapsed < Enough ? null : count / elapsed.TotalHours;

    /// <summary>
    /// How much longer the rest should take at the pace shown so far, nothing
    /// when there is no pace to go on, and zero when there is no rest.
    /// </summary>
    public static TimeSpan? TimeToGo(int done, int target, TimeSpan elapsed)
    {
        if (done >= target)
            return TimeSpan.Zero;

        if (done <= 0 || elapsed < Enough)
            return null;

        return elapsed * (target - done) / done;
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
