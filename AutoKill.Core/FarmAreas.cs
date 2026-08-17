using System.Numerics;

namespace AutoKill.Core;

/// <summary>Somewhere a mob can be farmed, and how thickly it spawns there.</summary>
/// <param name="Position">Where to path to, in world coordinates.</param>
/// <param name="MapPosition">
/// The same place in map coordinates. This is the pair the game shows and the
/// only one worth putting in front of a player, since it is what the map, the
/// minimap and every guide are written in.
/// </param>
public sealed record FarmLocation(
    uint TerritoryTypeId,
    string ZoneName,
    Vector3 Position,
    Vector2 MapPosition,
    int SpawnCount,
    ushort Level);

/// <summary>
/// A stretch of ground worth farming, and the spots inside it.
/// </summary>
/// <remarks>
/// A single spot is not how anyone farms. Mobs of one kind are spread over a
/// field in several loose knots, and the way to clear them is a circuit rather
/// than standing on one knot waiting. So spots that are close enough to patrol
/// between are gathered into an area, and the area is what gets chosen.
/// </remarks>
public sealed record FarmArea(
    uint TerritoryTypeId,
    string ZoneName,
    Vector3 Centre,
    Vector2 MapCentre,
    IReadOnlyList<FarmLocation> Spots)
{
    public int SpawnCount => Spots.Sum(s => s.SpawnCount);
}

/// <summary>One mob's spots, offered up to be merged with another's.</summary>
public sealed record MobSpots(uint BNpcNameId, IReadOnlyList<FarmLocation> Spots);

/// <summary>
/// A field, and every kind of mob standing in it that is worth killing.
/// </summary>
/// <param name="BNpcNameIds">Named by how much each contributes, thickest first.</param>
public sealed record SharedField(IReadOnlyList<uint> BNpcNameIds, FarmArea Area);

/// <summary>
/// Gathering spots into the ground a run actually covers.
/// </summary>
public static class FarmAreas
{
    /// <summary>
    /// Two spots this close are one knot seen twice. Well inside the distance a
    /// fight ranges over, so folding them costs nothing and saves flying the
    /// same twenty yalms twice under two names.
    /// </summary>
    private const float SameKnot = 25f;

    /// <summary>
    /// Gather one mob's spots into the areas they belong to, one territory at a
    /// time. Coordinates only compare within a territory, and no circuit crosses
    /// one.
    /// </summary>
    public static IReadOnlyList<FarmArea> IntoAreas(IReadOnlyList<FarmLocation> spots, float areaRadius) =>
        Gather(spots, areaRadius)
            .Select(gathered => gathered.Area)
            .OrderByDescending(area => area.SpawnCount)
            .ToList();

    /// <summary>
    /// The same gathering across several mobs at once, so a field they share is
    /// one place to go rather than one each.
    /// </summary>
    /// <remarks>
    /// Three kinds of petalouda drop the same scales and stand in the same two
    /// fields in Elpis. Farming one of them means flying past the other two, and
    /// waiting on their respawn timer while the field is full of the mobs that
    /// were not picked. What the run wants is the field.
    ///
    /// Spawn counts add up, because the useful number is how thickly the ground
    /// holds anything worth killing, not how thickly it holds one species.
    /// </remarks>
    public static IReadOnlyList<SharedField> Share(
        IReadOnlyList<MobSpots> mobs, float areaRadius, float knotRadius = SameKnot)
    {
        var knots = Knots(mobs, knotRadius);

        return Gather(knots.Select(knot => knot.Spot).ToList(), areaRadius)
            .Select(gathered => new SharedField(
                gathered.Members
                    .SelectMany(i => knots[i].Spawns)
                    .GroupBy(spawns => spawns.Key)
                    .OrderByDescending(named => named.Sum(spawns => spawns.Value))
                    .ThenBy(named => named.Key)
                    .Select(named => named.Key)
                    .ToList(),
                gathered.Area))
            .OrderByDescending(field => field.Area.SpawnCount)
            .ToList();
    }

    /// <summary>
    /// Every mob's spots as places to stand, with mobs on one knot folded into
    /// one place and remembering how many of each live there.
    /// </summary>
    private static List<Knot> Knots(IReadOnlyList<MobSpots> mobs, float knotRadius)
    {
        var all = mobs
            .SelectMany(mob => mob.Spots.Select(spot => (mob.BNpcNameId, Spot: spot)))
            .ToList();

        var knots = new List<Knot>();

        // Only within a territory. Two zones can share coordinate values
        // entirely, and nothing standing in one is standing in the other.
        foreach (var byTerritory in all.GroupBy(entry => entry.Spot.TerritoryTypeId))
        {
            var here = byTerritory.ToList();

            knots.AddRange(FarmSpots
                .GroupIndices(here.Select(entry => entry.Spot.Position).ToList(), knotRadius)
                .Select(indices => new Knot(
                    Fold(indices.Select(i => here[i].Spot).ToList()),
                    indices
                        .GroupBy(i => here[i].BNpcNameId)
                        .ToDictionary(named => named.Key, named => named.Sum(i => here[i].Spot.SpawnCount)))));
        }

        return knots;
    }

    /// <summary>
    /// Spots within reach of each other as one area, keeping where each came
    /// from so a caller can say what ended up in it.
    /// </summary>
    private static List<(IReadOnlyList<int> Members, FarmArea Area)> Gather(
        IReadOnlyList<FarmLocation> spots, float areaRadius)
    {
        var areas = new List<(IReadOnlyList<int> Members, FarmArea Area)>();

        foreach (var byTerritory in spots.Select((spot, i) => (Spot: spot, Index: i))
                     .GroupBy(entry => entry.Spot.TerritoryTypeId))
        {
            var here = byTerritory.ToList();

            foreach (var group in FarmSpots.GroupIndices(here.Select(entry => entry.Spot.Position).ToList(), areaRadius))
            {
                var members = group
                    .Select(i => here[i])
                    // Thickest first, since that is where a circuit should start.
                    .OrderByDescending(entry => entry.Spot.SpawnCount)
                    .ToList();

                var inside = members.Select(entry => entry.Spot).ToList();
                var first = inside[0];

                areas.Add((
                    members.Select(entry => entry.Index).ToList(),
                    new FarmArea(
                        first.TerritoryTypeId,
                        first.ZoneName,
                        new Vector3(
                            inside.Average(spot => spot.Position.X),
                            inside.Average(spot => spot.Position.Y),
                            inside.Average(spot => spot.Position.Z)),
                        new Vector2(
                            inside.Average(spot => spot.MapPosition.X),
                            inside.Average(spot => spot.MapPosition.Y)),
                        inside)));
            }
        }

        return areas;
    }

    /// <summary>
    /// Several mobs on one knot, as one place to stand. Weighted by how many of
    /// each live there, so the point lands where the killing is rather than
    /// halfway towards a lone straggler.
    /// </summary>
    private static FarmLocation Fold(IReadOnlyList<FarmLocation> spots)
    {
        if (spots.Count == 1)
            return spots[0];

        var total = spots.Sum(spot => spot.SpawnCount);
        var weights = total > 0
            ? spots.Select(spot => (double)spot.SpawnCount / total).ToList()
            : spots.Select(_ => 1d / spots.Count).ToList();

        double Weighted(Func<FarmLocation, float> of) =>
            spots.Select((spot, i) => of(spot) * weights[i]).Sum();

        return new FarmLocation(
            spots[0].TerritoryTypeId,
            spots[0].ZoneName,
            new Vector3(
                (float)Weighted(spot => spot.Position.X),
                (float)Weighted(spot => spot.Position.Y),
                (float)Weighted(spot => spot.Position.Z)),
            new Vector2(
                (float)Weighted(spot => spot.MapPosition.X),
                (float)Weighted(spot => spot.MapPosition.Y)),
            total,
            spots.Max(spot => spot.Level));
    }

    private sealed record Knot(FarmLocation Spot, IReadOnlyDictionary<uint, int> Spawns);
}
