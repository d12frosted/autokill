using System.Numerics;

namespace AutoKill.Core;

/// <summary>A place to stand, and the spawn points that cluster around it.</summary>
/// <param name="Members">
/// Which of the points given to the clusterer ended up here, by position in the
/// input. Kept because a caller usually knows something about each point that
/// the geometry does not, such as what level it was recorded at.
/// </param>
public sealed record FarmSpot(Vector3 Centre, IReadOnlyList<int> Members)
{
    public int Count => Members.Count;
}

/// <summary>
/// Groups scattered spawn points into places worth standing.
/// </summary>
/// <remarks>
/// A mob with forty recorded positions is not forty places to go. The points
/// sit in a handful of loose herds, and what a farming run wants is "stand
/// here, things respawn around you". Single link clustering matches that: two
/// points belong together if you could walk between them without leaving the
/// pull, and a chain of such points is one patrol route.
///
/// Grouping ignores elevation. Two points stacked on a cliff are still one
/// place to farm; working out how to get there is the navmesh's problem.
/// </remarks>
public static class FarmSpots
{
    public static IReadOnlyList<FarmSpot> Cluster(IReadOnlyList<Vector3> points, float radius) =>
        GroupIndices(points, radius)
            .Select(group => new FarmSpot(
                new Vector3(
                    group.Average(i => points[i].X),
                    group.Average(i => points[i].Y),
                    group.Average(i => points[i].Z)),
                group))
            .ToList();

    /// <summary>
    /// The same grouping, keeping the members. Spots are grouped into areas the
    /// same way points are grouped into spots, and an area is patrolled by
    /// visiting the spots that make it up, so which went where has to survive.
    /// </summary>
    /// <remarks>
    /// Members come back as positions in the input rather than as points. Two
    /// things can stand in exactly the same place, and once the spots of several
    /// mobs are put together that is ordinary rather than freakish. Matching a
    /// member back to what it came from by comparing positions would silently
    /// pick one of them and lose the rest.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<int>> GroupIndices(
        IReadOnlyList<Vector3> points, float radius)
    {
        if (points.Count == 0)
            return [];

        var parent = new int[points.Count];
        for (var i = 0; i < parent.Length; i++)
            parent[i] = i;

        int Find(int i)
        {
            while (parent[i] != i)
                i = parent[i] = parent[parent[i]];
            return i;
        }

        void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb)
                parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }

        var radiusSquared = radius * radius;
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                var dx = points[i].X - points[j].X;
                var dz = points[i].Z - points[j].Z;
                if (dx * dx + dz * dz <= radiusSquared)
                    Union(i, j);
            }
        }

        var groups = new Dictionary<int, List<int>>();
        for (var i = 0; i < points.Count; i++)
        {
            if (!groups.TryGetValue(Find(i), out var group))
                groups[Find(i)] = group = [];
            group.Add(i);
        }

        return groups.Values
            // Densest first, then by position so output does not depend on input order.
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Average(i => points[i].X))
            .ThenBy(group => group.Average(i => points[i].Z))
            .Cast<IReadOnlyList<int>>()
            .ToList();
    }
}
