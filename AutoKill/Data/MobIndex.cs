using System.Numerics;
using AutoKill.Core;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;

namespace AutoKill.Data;

/// <summary>Somewhere a mob can be farmed, and how thickly it spawns there.</summary>
public sealed record FarmLocation(
    uint TerritoryTypeId,
    string ZoneName,
    Vector3 Position,
    int SpawnCount,
    ushort Level);

public sealed record MobEntry(
    uint BNpcNameId,
    string Name,
    IReadOnlyList<uint> BaseIds,
    IReadOnlyList<FarmLocation> Locations,
    IReadOnlyList<uint> Drops)
{
    public bool Farmable => Locations.Count > 0;
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
/// coordinates. The third component is kept only as a hint: elevation in this
/// data is not reliable enough to path to, so the caller snaps to the navmesh
/// floor before moving.
/// </remarks>
public sealed class MobIndex
{
    private readonly Dictionary<uint, MobEntry> byNameId;
    private readonly Dictionary<uint, List<uint>> mobsByItem;
    private readonly Dictionary<uint, string> droppableItemNames;

    private MobIndex(
        Dictionary<uint, MobEntry> byNameId,
        Dictionary<uint, List<uint>> mobsByItem,
        Dictionary<uint, string> droppableItemNames)
    {
        this.byNameId = byNameId;
        this.mobsByItem = mobsByItem;
        this.droppableItemNames = droppableItemNames;
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
        var elevations = new Dictionary<(uint NameId, uint Territory), List<float>>();
        var baseIds = new Dictionary<uint, HashSet<uint>>();

        void Record(uint nameId, uint territoryId, Vector3 world, float? elevation)
        {
            var key = (nameId, territoryId);
            if (!grouped.TryGetValue(key, out var points))
                grouped[key] = points = [];
            points.Add(world);

            if (elevation is not { } value)
                return;
            if (!elevations.TryGetValue(key, out var known))
                elevations[key] = known = [];
            known.Add(value);
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

            var map = territory.Map.Value;
            Record(
                spawn.BNpcNameId,
                spawn.TerritoryTypeId,
                new Vector3(
                    (float)MapCoordinates.ToWorld(spawn.Position.X, map.SizeFactor, map.OffsetX),
                    0f,
                    (float)MapCoordinates.ToWorld(spawn.Position.Y, map.SizeFactor, map.OffsetY)),
                spawn.Position.Z);

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
                        (float)MapCoordinates.ToWorld(point[2], map.SizeFactor, map.OffsetY)),
                    null);
                added++;
            }
        }

        log.Information($"Spawn points: {spawns.Count} supplemental, {added} embedded.");

        var dropsByMob = new Dictionary<uint, HashSet<uint>>();
        var mobsByItem = new Dictionary<uint, List<uint>>();
        var droppableItemNames = new Dictionary<uint, string>();

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

            // Only one of the two sources carries elevation, so it is taken per
            // territory rather than per cluster. It is a starting height for the
            // navmesh floor query, not somewhere to path to.
            var elevation = elevations.TryGetValue((nameId, territoryId), out var known) && known.Count > 0
                ? known.Average()
                : 0f;

            foreach (var spot in FarmSpots.Cluster(points, clusterRadius))
            {
                var centre = spot.Centre with { Y = elevation };
                locations.Add(new FarmLocation(territoryId, zone, centre, spot.Count, 0));
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
            locations.Sort((a, b) => b.SpawnCount.CompareTo(a.SpawnCount));

            byNameId[nameId] = new MobEntry(
                nameId,
                label,
                baseIds.GetValueOrDefault(nameId)?.ToList() ?? [],
                locations,
                dropsByMob.GetValueOrDefault(nameId)?.ToList() ?? []);
        }

        log.Information(
            $"Mob index: {byNameId.Count} mobs, "
            + $"{byNameId.Values.Count(m => m.Farmable)} farmable, "
            + $"{droppableItemNames.Count} droppable items.");

        return new MobIndex(byNameId, mobsByItem, droppableItemNames);
    }

    public MobEntry? Get(uint bNpcNameId) => byNameId.GetValueOrDefault(bNpcNameId);

    /// <summary>Mobs whose name contains the query, the ones you can actually reach first.</summary>
    public IReadOnlyList<MobEntry> SearchMobs(string query, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        return byNameId.Values
            .Where(mob => mob.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(mob => mob.Farmable)
            .ThenByDescending(mob => mob.Locations.Sum(l => l.SpawnCount))
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
            .ThenByDescending(mob => mob.Locations.Count > 0 ? mob.Locations[0].SpawnCount : 0)
            .ThenByDescending(mob => mob.Locations.Sum(l => l.SpawnCount))
            .ToList();
    }
}
