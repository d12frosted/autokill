using System.Numerics;
using AutoKill.Core;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;

namespace AutoKill.Data;

/// <summary>What turns world coordinates into the ones the map shows.</summary>
internal readonly record struct MapProjection(ushort SizeFactor, short OffsetX, short OffsetY);

public sealed record MobEntry(
    uint BNpcNameId,
    string Name,
    IReadOnlyList<uint> BaseIds,
    IReadOnlyList<FarmArea> Areas,
    IReadOnlyList<uint> Drops)
{
    public bool Farmable => Areas.Count > 0;

    /// <summary>
    /// How hard it is everywhere it stands, or nothing when nobody recorded it.
    /// </summary>
    /// <remarks>
    /// One name can cover creatures of very different levels: the same aldgoat
    /// stands in a starting zone and in a much later one. So this is the whole
    /// span, and each area says what its own patch of ground is.
    /// </remarks>
    public LevelRange? Level =>
        LevelRange.Of(Areas.SelectMany(area => area.Spots).Select(spot => spot.Level));
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
///
/// Only the embedded half carries levels. A point from the other one has none,
/// which is why a level of zero has to keep meaning "unrecorded" all the way
/// through rather than being folded into the arithmetic.
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
    private readonly Dictionary<uint, string> zoneNames;
    private readonly Dictionary<uint, MapProjection> projections;

    // Fields worked out on demand and kept, keyed by the mobs they were worked
    // out for. Drawn from the window thread only.
    private readonly Dictionary<string, IReadOnlyList<FarmTarget>> fields = [];

    private MobIndex(
        Dictionary<uint, MobEntry> byNameId,
        Dictionary<uint, List<uint>> mobsByItem,
        Dictionary<uint, string> droppableItemNames,
        Dictionary<uint, ushort> droppableItemIcons,
        Dictionary<uint, string> zoneNames,
        Dictionary<uint, MapProjection> projections)
    {
        this.byNameId = byNameId;
        this.mobsByItem = mobsByItem;
        this.droppableItemNames = droppableItemNames;
        this.droppableItemIcons = droppableItemIcons;
        this.zoneNames = zoneNames;
        this.projections = projections;
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
        var grouped = new Dictionary<(uint NameId, uint Territory), List<(Vector3 World, ushort Level)>>();
        var baseIds = new Dictionary<uint, HashSet<uint>>();

        void Record(uint nameId, uint territoryId, Vector3 world, ushort level)
        {
            var key = (nameId, territoryId);
            if (!grouped.TryGetValue(key, out var points))
                grouped[key] = points = [];
            points.Add((world, level));
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
                    (float)MapCoordinates.ToWorld(spawn.Position.Y, map.SizeFactor, map.OffsetY)),
                // This source has no levels at all.
                0);

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
                    // Fourth column since format 2. An older payload has three
                    // and simply says nothing about how hard anything is.
                    point.Length > 3 ? (ushort)point[3] : (ushort)0);
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
        var zoneNames = new Dictionary<uint, string>();

        // Kept so that somewhere learnt at runtime, like a FATE that is up right
        // now, can be shown in the coordinates the map uses.
        var projections = new Dictionary<uint, MapProjection>();
        foreach (var territory in territories)
        {
            if (territory.Map.ValueNullable is { } m && territory.RowId != 0)
                projections[territory.RowId] = new MapProjection(m.SizeFactor, m.OffsetX, m.OffsetY);
        }
        foreach (var ((nameId, territoryId), seen) in grouped)
        {
            if (!territories.TryGetRow(territoryId, out var territory))
                continue;

            var zone = territory.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty;
            zoneNames[territoryId] = zone;

            if (!locationsByMob.TryGetValue(nameId, out var locations))
                locationsByMob[nameId] = locations = [];

            var map = territory.Map.ValueNullable;
            var points = seen.Select(entry => entry.World).ToList();

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

                // The hardest of what stands here, since walking in means
                // meeting whichever of them is worst.
                locations.Add(new FarmLocation(
                    territoryId, zone, centre, onMap, spot.Count,
                    spot.Members.Max(i => seen[i].Level)));
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

            // The game keeps these in lower case. Read one at a time that is
            // fine; read as a list it is a wall with nothing for the eye to
            // catch on. Searching stays case insensitive either way.
            label = Phrases.Capitalise(label);

            var locations = locationsByMob.GetValueOrDefault(nameId) ?? [];

            byNameId[nameId] = new MobEntry(
                nameId,
                label,
                baseIds.GetValueOrDefault(nameId)?.ToList() ?? [],
                FarmAreas.IntoAreas(locations, AreaRadius),
                dropsByMob.GetValueOrDefault(nameId)?.ToList() ?? []);
        }

        log.Information(
            $"Mob index: {byNameId.Count} mobs, "
            + $"{byNameId.Values.Count(m => m.Farmable)} farmable, "
            + $"{droppableItemNames.Count} droppable items.");

        return new MobIndex(
            byNameId, mobsByItem, droppableItemNames, droppableItemIcons, zoneNames, projections);
    }

    public MobEntry? Get(uint bNpcNameId) => byNameId.GetValueOrDefault(bNpcNameId);

    /// <summary>Somewhere to farm that is not in the shipped data at all.</summary>
    /// <remarks>
    /// A FATE that is running is a better answer than anything recorded: it is
    /// where the mob is right now rather than where it was once seen. One spot,
    /// since a FATE is one place.
    /// </remarks>
    public FarmArea AreaAt(uint territoryId, Vector3 position)
    {
        var zone = ZoneName(territoryId);
        var onMap = ToMap(territoryId, position);
        return new FarmArea(
            territoryId,
            zone,
            position,
            onMap,
            [new FarmLocation(territoryId, zone, position, onMap, 1, 0)]);
    }

    public Vector2 ToMap(uint territoryId, Vector3 world) =>
        projections.TryGetValue(territoryId, out var map)
            ? new Vector2(
                (float)MapCoordinates.ToMap(world.X, map.SizeFactor, map.OffsetX),
                (float)MapCoordinates.ToMap(world.Z, map.SizeFactor, map.OffsetY))
            : Vector2.Zero;

    /// <summary>The name of a droppable item, for picking one out of a list.</summary>
    public string ItemName(uint itemId) =>
        droppableItemNames.TryGetValue(itemId, out var name) ? name : $"item {itemId}";

    /// <summary>The item's icon id, or zero when there is nothing to draw.</summary>
    public ushort ItemIcon(uint itemId) => droppableItemIcons.GetValueOrDefault(itemId);

    /// <summary>The name of a zone, for naming things learnt elsewhere.</summary>
    public string ZoneName(uint territoryId) =>
        zoneNames.TryGetValue(territoryId, out var name) && name.Length > 0
            ? name
            : $"territory {territoryId}";

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

    /// <summary>
    /// Whether anything worth going to drops this, without working out what.
    /// Asked of every material on a list, so it may not allocate to answer.
    /// </summary>
    public bool AnythingDrops(uint itemId) =>
        mobsByItem.TryGetValue(itemId, out var mobs) && mobs.Any(byNameId.ContainsKey);

    /// <summary>
    /// The same mobs as places rather than as species: every field where
    /// something dropping this item stands, thickest first, each carrying all
    /// the kinds standing in it.
    /// </summary>
    /// <remarks>
    /// Someone searching for an item wants the item. Three kinds of petalouda
    /// drop the same scales in the same two fields in Elpis, and offering them
    /// one at a time means whichever is picked, two thirds of the field gets
    /// flown past.
    /// </remarks>
    public IReadOnlyList<FarmTarget> FieldsDropping(uint itemId) =>
        Fields(MobsDropping(itemId).Where(mob => mob.Farmable).ToList());

    /// <summary>
    /// Any set of mobs as the fields they share, so ground held by several of
    /// them is one place to go.
    /// </summary>
    public IReadOnlyList<FarmTarget> Fields(IReadOnlyList<MobEntry> mobs)
    {
        if (mobs.Count == 0)
            return [];

        // Asked once per frame while a list is on screen, and clustering every
        // spot of every mob that drops something common is far too much work to
        // do sixty times a second. Nothing here changes after loading, so the
        // answer keeps.
        var key = string.Join(',', mobs.Select(mob => mob.BNpcNameId).Order());
        if (fields.TryGetValue(key, out var known))
            return known;

        var spots = mobs
            .Select(mob => new MobSpots(
                mob.BNpcNameId,
                mob.Areas.SelectMany(area => area.Spots).ToList()))
            .ToList();

        return fields[key] = FarmAreas.Share(spots, AreaRadius)
            .Select(field => new FarmTarget(
                field.BNpcNameIds.Select(id => byNameId[id]).ToList(),
                field.Area))
            .ToList();
    }
}
