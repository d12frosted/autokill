namespace AutoKill.Core;

/// <summary>
/// How long a place is expected to take to come back, and what says so.
/// </summary>
/// <remarks>
/// Two things get measured while farming, and they are not the same quantity.
/// A spawn point timed from a death to the next thing standing on it is the
/// respawn itself. A spot stamped on leaving and closed on returning carries
/// the whole trip back inside it, so it always reads long.
///
/// Both answer the question a circuit asks, which is when somewhere is worth
/// going back to, but one of them answers it without a journey mixed in. So
/// timings win as soon as there are enough of them, and until then the return
/// trips carry it: an inflated number is still better than the flat guess a
/// run falls back on.
/// </remarks>
/// <param name="Samples">How many measurements this rests on.</param>
/// <param name="Timed">
/// Whether it came from timing spawn points rather than from return trips,
/// which is the difference between a respawn and a round of the circuit.
/// </param>
public readonly record struct Repopulation(TimeSpan Typical, int Samples, bool Timed)
{
    /// <summary>
    /// How many measurements it takes to believe any of them. Fewer than this
    /// is a coincidence, and a circuit is better off with its default than with
    /// a number that came out of one bad minute.
    /// </summary>
    public const int Enough = 3;

    public static Repopulation? From(IReadOnlyList<double> timed, IReadOnlyList<double> returned) =>
        Middle(timed, true) ?? Middle(returned, false);

    /// <summary>
    /// The middle of what was seen, which ignores the long tail a mean would
    /// chase: one slow measurement is a stretch of bad luck, not a slower zone.
    /// </summary>
    private static Repopulation? Middle(IReadOnlyList<double> seconds, bool timed)
    {
        if (seconds.Count < Enough)
            return null;

        var sorted = seconds.Order().ToList();
        var half = sorted.Count / 2;
        var middle = sorted.Count % 2 == 1
            ? sorted[half]
            : (sorted[half - 1] + sorted[half]) / 2.0;

        return new Repopulation(TimeSpan.FromSeconds(middle), sorted.Count, timed);
    }
}
