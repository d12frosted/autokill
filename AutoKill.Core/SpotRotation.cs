namespace AutoKill.Core;

/// <summary>One knot of a circuit, how long since it was emptied, and how far off.</summary>
/// <param name="SinceCleared">Null when it has never been cleared at all.</param>
/// <param name="Distance">Yalms from wherever the character is now.</param>
public readonly record struct SpotState(
    int Index,
    int SpawnCount,
    TimeSpan? SinceCleared,
    float Distance = 0f);

/// <summary>
/// Deciding where to go next round a farming circuit.
/// </summary>
/// <remarks>
/// A fixed rotation is easy but wrong twice over. It arrives at spots that were
/// emptied moments ago, and it walks the same loop in the same order all day,
/// which is exactly what it looks like.
///
/// So spots are scored on how far along they are towards repopulating, weighted
/// by how many things live there and divided by what it costs to get to them,
/// and close calls are settled by chance rather than by a rule.
///
/// Distance matters because a scattered field is mostly travel: one run over
/// twelve knots spent nearly half its time getting between them. It is damped
/// rather than linear, so a full field a little further off still beats an empty
/// one underfoot, and the circuit does not settle into the nearest corner.
/// </remarks>
public static class SpotRotation
{
    public static int PickNext(
        IReadOnlyList<SpotState> spots,
        int current,
        TimeSpan expectedRespawn,
        double jitter,
        int? seed = null)
    {
        if (spots.Count == 0)
            return current;
        if (spots.Count == 1)
            return spots[0].Index;

        var random = seed is { } value ? new Random(value) : Random.Shared;

        var best = current;
        var bestScore = double.MinValue;

        foreach (var spot in spots)
        {
            // Somewhere just left is the one place definitely not worth
            // returning to.
            if (spot.Index == current)
                continue;

            // Never cleared beats everything: it has not been touched, so all of
            // it is still standing.
            // Past a full respawn there is nothing more to gain from waiting,
            // which is what keeps a big spot emptied seconds ago from outranking
            // a small one that is actually ready.
            var score = Score(spot, expectedRespawn);

            if (jitter > 0)
                score *= 1.0 + ((random.NextDouble() - 0.5) * 2.0 * jitter);

            if (score <= bestScore)
                continue;

            bestScore = score;
            best = spot.Index;
        }

        return best;
    }

    /// <summary>
    /// What a spot is worth, before any jitter. Public so a run can record why
    /// it went where it went rather than leaving the choice unexplained.
    /// </summary>
    public static double Score(SpotState spot, TimeSpan expectedRespawn)
    {
        var respawn = expectedRespawn.TotalSeconds <= 0 ? 1.0 : expectedRespawn.TotalSeconds;

        // Never cleared beats everything: it has not been touched, so all of it
        // is still standing. Past a full respawn there is nothing more to gain
        // from waiting, which is what keeps a big spot emptied seconds ago from
        // outranking a small one that is actually ready.
        var readiness = spot.SinceCleared is { } since
            ? Math.Clamp(since.TotalSeconds / respawn, 0.0, 1.0)
            : 1.0;

        var score = readiness * Math.Max(1, spot.SpawnCount);
        if (spot.SinceCleared is null)
            score *= 2.0;

        // Square root, so crossing a zone costs something without the nearest
        // spot winning every time regardless of what is on it. The constant
        // keeps anything within a short walk near enough free.
        return score / Math.Sqrt(1.0 + (Math.Max(0f, spot.Distance) / 100.0));
    }
}
