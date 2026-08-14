using System.Numerics;
using AutoKill.Core;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;

namespace AutoKill.Data;

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

public sealed record MobEntry(
    uint BNpcNameId,
    string Name,
    IReadOnlyList<uint> BaseIds,
    IReadOnlyList<FarmArea> Areas,
    IReadOnlyList<uint> Drops)
{
    public bool Farmable => Areas.Count > 0;
}

/// <summary>
/// Every mob worth knowing about: what it is called, where it stands and what
/// it drops.
/// </summary>
/// <remarks>
/// None of this is in the game's sheets. Loot tables are server side and mobs
/// are spawned by the server, so both halves come from LuminaSupplemental's
/// community-collected CSVs and are joined here on BNpcName.
///
/// Spawn positions arrive map projected, so X and Y are converted to world
/// coordinates. There is no height in them: what looks like a third dimension
/// is the second one already converted. Callers drop the point onto the navmesh
/// to find the ground.
/// </remarks>
public sealed class MobIndex
{
    // Spots this far apart are still one circuit. Wide enough to take in a
    // field of mobs, narrow enough not to swallow the whole zone.
    private const float AreaRadius = 250f;

    private readonly Dictionary<uint, MobEntry> byNameId;
    private readonly Dictionary<uint, List<uint>> mobsByItem;
    private readonly Dictionary<uint, string> droppableItemNames;
    private readonly Dictionary<uint, ushort> droppableItemIcons;

    private MobIndex(
        Dictionary<uint, MobEntry> byNameId,
        Dictionary<uint, List<uint>> mobsByItem,
        Dictionary<uint, string> droppableItemNames,
        Dictionary<uint, ushort> droppableItemIcons)
    {
        this.byNameId = byNameId;
        this.mobsByItem = mobsByItem;
        this.droppableItemNames = droppableItemNames;
        this.droppableItemIcons = droppableItemIcons;
    }

    public IReadOnlyCollection<MobEntry> Mobs => byNameId.Values;

    public int DroppableItemCount => droppableItemNames.Count;

    public static MobIndex Build(IDataManager data, IPluginLog log, float clusterRadius = 50f)
    {
        var gameData = data.GameData;

        var spawns = CsvLoader.LoadResource<MobSpawnPosition>(
            CsvLoader.MobSpawnResourceName, true, out _, out var spawnErrors, gameData);
        var drops = CsvLoader.LoadResource<MobDrop>(
            CsvLoader.MobDropResourceName, true, out _, out var dropErrors, gameData);

        if (spawnErrors.Count > 0 || dropErrors.Count > 0)
            log.Warning($"Supplemental data loaded with {spawnErrors.Count + dropErrors.Count} error(s).");

        var territories = data.GetExcelSheet<TerritoryType>();
        var names = data.GetExcelSheet<BNpcName>();
        var items = data.GetExcelSheet<Item>();

        // Spawn points only mean anything within one territory's projection, and
        // two territories can share coordinate values entirely.
        var grouped = new Dictionary<(uint NameId, uint Territory), List<Vector3>>();
        var baseIds = new Dictionary<uint, HashSet<uint>>();

        void Record(uint nameId, uint territoryId, Vector3 world)
        {
            var key = (nameId, territoryId);
            if (!grouped.TryGetValue(key, out var points))
                grouped[key] = points = [];
            points.Add(world);
        }

        foreach (var spawn in spawns)
        {
            if (spawn.BNpcNameId == 0 || spawn.TerritoryTypeId == 0)
                continue;

            if (SpawnPositions.IsUnknown(spawn.Position.X, spawn.Position.Y, spawn.Position.Z))
                continue;

            if (!territories.TryGetRow(spawn.TerritoryTypeId, out var territory))
                continue;
            if (!territory.Map.ValueNullable.HasValue)
                continue;

            // Only X and Y carry anything. The third component is the same as
              // the second, already converted to world coordinates, so there is
              // no height in this data at all and none should be invented.
            var map = territory.Map.Value;
            Record(
                spawn.BNpcNameId,
                spawn.TerritoryTypeId,
                new Vector3(
                    (float)MapCoordinates.ToWorld(spawn.Position.X, map.SizeFactor, map.OffsetX),
                    0f,
                    (float)MapCoordinates.ToWorld(spawn.Position.Y, map.SizeFactor, map.OffsetY)));

            if (spawn.BNpcBaseId != 0)
            {
                if (!baseIds.TryGetValue(spawn.BNpcNameId, out var set))
                    baseIds[spawn.BNpcNameId] = set = [];
                set.Add(spawn.BNpcBaseId);
            }
        }

        // The dense half. Keyed by map rather than territory, and carrying no
        // elevation, so the map row supplies both.
        var mapSheet = data.GetExcelSheet<Map>();
        var added = 0;
        foreach (var (nameId, points) in EmbeddedPositions.Load())
        {
            foreach (var point in points)
            {
                if (point.Length < 3)
                    continue;
                if (!mapSheet.TryGetRow((uint)point[0], out var map))
                    continue;

                var territoryId = map.TerritoryType.RowId;
                if (territoryId == 0)
                    continue;

                Record(
                    nameId,
                    territoryId,
                    new Vector3(
                        (float)MapCoordinates.ToWorld(point[1], map.SizeFactor, map.OffsetX),
                        0f,
                        (float)MapCoordinates.ToWorld(point[2], map.SizeFactor, map.OffsetY)));
                added++;
            }
        }

        log.Information($"Spawn points: {spawns.Count} supplemental, {added} embedded.");

        var dropsByMob = new Dictionary<uint, HashSet<uint>>();
        var mobsByItem = new Dictionary<uint, List<uint>>();
        var droppableItemNames = new Dictionary<uint, string>();
        var droppableItemIcons = new Dictionary<uint, ushort>();

        foreach (var drop in drops)
        {
            if (drop.ItemId == 0 || drop.BNpcNameId == 0)
                continue;

            if (!dropsByMob.TryGetValue(drop.BNpcNameId, out var set))
                dropsByMob[drop.BNpcNameId] = set = [];
            set.Add(drop.ItemId);

            if (!mobsByItem.TryGetValue(drop.ItemId, out var mobs))
                mobsByItem[drop.ItemId] = mobs = [];
            if (!mobs.Contains(drop.BNpcNameId))
                mobs.Add(drop.BNpcNameId);

            if (!droppableItemNames.ContainsKey(drop.ItemId)
                && items.TryGetRow(drop.ItemId, out var item))
            {
                droppableItemNames[drop.ItemId] = item.Name.ExtractText();
                droppableItemIcons[drop.ItemId] = item.Icon;
            }
        }

        var locationsByMob = new Dictionary<uint, List<FarmLocation>>();
        foreach (var ((nameId, territoryId), points) in grouped)
        {
            if (!territories.TryGetRow(territoryId, out var territory))
                continue;

            var zone = territory.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty;

            if (!locationsByMob.TryGetValue(nameId, out var locations))
                locationsByMob[nameId] = locations = [];

            var map = territory.Map.ValueNullable;

            foreach (var spot in FarmSpots.Cluster(points, clusterRadius))
            {
                // Height stays at zero deliberately. Neither source has any, and
                // the caller drops the point onto the navmesh instead.
                var centre = spot.Centre;
                var onMap = map is { } m
                    ? new Vector2(
                        (float)MapCoordinates.ToMap(centre.X, m.SizeFactor, m.OffsetX),
                        (float)MapCoordinates.ToMap(centre.Z, m.SizeFactor, m.OffsetY))
                    : Vector2.Zero;

                locations.Add(new FarmLocation(territoryId, zone, centre, onMap, spot.Count, 0));
            }
        }

        var byNameId = new Dictionary<uint, MobEntry>();
        foreach (var nameId in locationsByMob.Keys.Concat(dropsByMob.Keys).Distinct())
        {
            if (!names.TryGetRow(nameId, out var name))
                continue;

            var label = name.Singular.ExtractText();
            if (string.IsNullOrWhiteSpace(label))
                continue;

            var locations = locationsByMob.GetValueOrDefault(nameId) ?? [];

            byNameId[nameId] = new MobEntry(
                nameId,
                label,
                baseIds.GetValueOrDefault(nameId)?.ToList() ?? [],
                IntoAreas(locations),
                dropsByMob.GetValueOrDefault(nameId)?.ToList() ?? []);
        }

        log.Information(
            $"Mob index: {byNameId.Count} mobs, "
            + $"{byNameId.Values.Count(m => m.Farmable)} farmable, "
            + $"{droppableItemNames.Count} droppable items.");

        return new MobIndex(byNameId, mobsByItem, droppableItemNames, droppableItemIcons);
    }

    /// <summary>
    /// Gather spots into the areas they belong to, one territory at a time.
    /// Coordinates only compare within a territory, and no circuit crosses one.
    /// </summary>
    private static IReadOnlyList<FarmArea> IntoAreas(List<FarmLocation> spots)
    {
        var areas = new List<FarmArea>();

        foreach (var byTerritory in spots.GroupBy(s => s.TerritoryTypeId))
        {
            var inTerritory = byTerritory.ToList();
            var centres = inTerritory.Select(s => s.Position).ToList();

            foreach (var group in FarmSpots.Group(centres, AreaRadius))
            {
                var members = group
                    .Select(point => inTerritory.First(s => s.Position == point))
                    .OrderByDescending(s => s.SpawnCount)
                    .ToList();

                var first = members[0];
                areas.Add(new FarmArea(
                    first.TerritoryTypeId,
                    first.ZoneName,
                    new Vector3(group.Average(p => p.X), group.Average(p => p.Y), group.Average(p => p.Z)),
                    new Vector2(members.Average(s => s.MapPosition.X), members.Average(s => s.MapPosition.Y)),
                    members));
            }
        }

        return areas.OrderByDescending(a => a.SpawnCount).ToList();
    }

    public MobEntry? Get(uint bNpcNameId) => byNameId.GetValueOrDefault(bNpcNameId);

    /// <summary>The name of a droppable item, for picking one out of a list.</summary>
    public string ItemName(uint itemId) =>
        droppableItemNames.TryGetValue(itemId, out var name) ? name : $"item {itemId}";

    /// <summary>The item's icon id, or zero when there is nothing to draw.</summary>
    public ushort ItemIcon(uint itemId) => droppableItemIcons.GetValueOrDefault(itemId);

    /// <summary>Mobs whose name contains the query, the ones you can actually reach first.</summary>
    public IReadOnlyList<MobEntry> SearchMobs(string query, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        return byNameId.Values
            .Where(mob => mob.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(mob => mob.Farmable)
            .ThenByDescending(mob => mob.Areas.Sum(a => a.SpawnCount))
            .ThenBy(mob => mob.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    /// <summary>Droppable items whose name contains the query.</summary>
    public IReadOnlyList<(uint ItemId, string Name)> SearchItems(string query, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        return droppableItemNames
            .Where(pair => pair.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Value.Length)
            .ThenBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(pair => (pair.Key, pair.Value))
            .ToList();
    }

    /// <summary>
    /// Mobs that drop an item, best farmed first. Density and cluster tightness
    /// decide the order, so a mob with fourteen spawns in one field outranks one
    /// with a single sighting.
    /// </summary>
    public IReadOnlyList<MobEntry> MobsDropping(uint itemId)
    {
        if (!mobsByItem.TryGetValue(itemId, out var mobs))
            return [];

        return mobs
            .Select(Get)
            .Where(mob => mob is not null)
            .Select(mob => mob!)
            .OrderByDescending(mob => mob.Farmable)
            .ThenByDescending(mob => mob.Areas.Count > 0 ? mob.Areas[0].SpawnCount : 0)
            .ThenByDescending(mob => mob.Areas.Sum(a => a.SpawnCount))
            .ToList();
    }
}
