namespace AutoKill.Core;

/// <summary>One knot of a circuit, and how long since it was emptied.</summary>
/// <param name="SinceCleared">Null when it has never been cleared at all.</param>
public readonly record struct SpotState(int Index, int SpawnCount, TimeSpan? SinceCleared);

/// <summary>
/// Deciding where to go next round a farming circuit.
/// </summary>
/// <remarks>
/// A fixed rotation is easy but wrong twice over. It arrives at spots that were
/// emptied moments ago, and it walks the same loop in the same order all day,
/// which is exactly what it looks like.
///
/// So spots are scored on how far along they are towards repopulating, weighted
/// by how many things live there, and close calls are settled by chance rather
/// than by a rule.
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
        var respawn = expectedRespawn.TotalSeconds <= 0 ? 1.0 : expectedRespawn.TotalSeconds;

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
            var readiness = spot.SinceCleared is { } since
                ? Math.Clamp(since.TotalSeconds / respawn, 0.0, 1.0)
                : 1.0;

            var score = readiness * Math.Max(1, spot.SpawnCount);
            if (spot.SinceCleared is null)
                score *= 2.0;

            if (jitter > 0)
                score *= 1.0 + ((random.NextDouble() - 0.5) * 2.0 * jitter);

            if (score <= bestScore)
                continue;

            bestScore = score;
            best = spot.Index;
        }

        return best;
    }
}
