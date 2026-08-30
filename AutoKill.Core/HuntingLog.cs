namespace AutoKill.Core;

/// <summary>One mob a hunting log entry asks for, and how far along it is.</summary>
public sealed record HuntingLogKill(uint BNpcNameId, string Name, int Needed, int Killed)
{
    public int Remaining => Math.Max(0, Needed - Killed);

    public bool Done => Remaining == 0;
}

/// <summary>
/// One line of the hunting log: a few mobs, and how many of each.
/// </summary>
/// <param name="Index">
/// Where it sits in its log, from 1 to 50. On a class log that is also the
/// level it was written for.
/// </param>
/// <param name="Level">
/// The level it is meant for, or nothing when nobody can say. A Grand Company
/// log does not climb a level at a time, so its entries fall back to whatever
/// the ground they send you to was recorded at.
/// </param>
public sealed record HuntingLogEntry(
    uint RowId,
    int Index,
    int Rank,
    int? Level,
    IReadOnlyList<HuntingLogKill> Kills)
{
    public bool Done => Kills.All(kill => kill.Done);

    public int Remaining => Kills.Sum(kill => kill.Remaining);

    /// <summary>What it asks for in total, done or not.</summary>
    public int Needed => Kills.Sum(kill => kill.Needed);
}

/// <summary>One mob the log wants, and every zone it could be killed in.</summary>
/// <remarks>
/// A mob rather than an entry, because an entry naming two mobs does not
/// promise they stand in the same zone, and a trip is decided by where
/// something stands rather than by which line of the log asked for it.
/// </remarks>
public readonly record struct HuntingLogPlacement(
    uint BNpcNameId, IReadOnlyList<uint> TerritoryTypeIds);

/// <summary>One trip: a zone, and the mobs it settles.</summary>
public sealed record HuntingLogStop(uint TerritoryTypeId, IReadOnlyList<uint> BNpcNameIds);

/// <summary>
/// Working out what a rank of the hunting log costs to finish.
/// </summary>
/// <remarks>
/// A rank is ten entries of three or four kills. Run one at a time that is ten
/// teleports for thirty-odd kills, and most of the run is the travelling. The
/// entries of a rank sit in about seven zones between them, and about five
/// zones is enough to reach all of them, so grouping by zone halves the trips
/// before anything has to fly anywhere.
/// </remarks>
public static class HuntingLogPlan
{
    /// <summary>
    /// Whether an entry is close enough to the class's own level to be worth
    /// sending it at.
    /// </summary>
    /// <remarks>
    /// An entry nobody can put a level on blocks nothing, for the same reason
    /// an unrecorded mob level blocks nothing: silence is not a number, and
    /// treating it as zero would hide half the log.
    /// </remarks>
    public static bool WithinReach(int? entryLevel, int classLevel, int allowance) =>
        entryLevel is not { } level || level <= classLevel + allowance;

    /// <summary>
    /// The fewest zones that between them hold every mob, most crowded first.
    /// </summary>
    /// <remarks>
    /// Greedy, which is not always the true minimum. A rank is a couple of
    /// dozen mobs over a handful of zones, where greedy is nearly always exact
    /// and always close, and a plan somebody can follow in their head is worth
    /// more here than the last saved teleport.
    ///
    /// Ties break on the lowest territory id rather than on what order the
    /// mobs arrived in, because the same rank planned twice has to come out
    /// the same way.
    /// </remarks>
    public static IReadOnlyList<HuntingLogStop> Stops(
        IReadOnlyList<HuntingLogPlacement> wanted)
    {
        var left = wanted.Where(mob => mob.TerritoryTypeIds.Count > 0).ToList();
        var stops = new List<HuntingLogStop>();

        while (left.Count > 0)
        {
            var best = left
                .SelectMany(mob => mob.TerritoryTypeIds)
                .Distinct()
                .OrderByDescending(territory => left.Count(
                    mob => mob.TerritoryTypeIds.Contains(territory)))
                .ThenBy(territory => territory)
                .First();

            var taken = left.Where(mob => mob.TerritoryTypeIds.Contains(best)).ToList();
            stops.Add(new HuntingLogStop(best, taken.Select(mob => mob.BNpcNameId).ToList()));
            left.RemoveAll(mob => mob.TerritoryTypeIds.Contains(best));
        }

        return stops;
    }
}
