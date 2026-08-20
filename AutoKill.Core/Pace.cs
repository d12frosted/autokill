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
}
