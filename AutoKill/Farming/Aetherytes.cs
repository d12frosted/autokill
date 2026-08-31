using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace AutoKill.Farming;

/// <summary>
/// The way into a zone: the aetheryte to teleport to, and the aethernet gates
/// to take once the character has landed.
/// </summary>
/// <param name="TerritoryTypeId">
/// Where the teleport itself lands, which is the target zone for every route
/// but a crossing.
/// </param>
/// <param name="Gates">
/// Empty when the aetheryte stands in the zone being farmed, which is the
/// ordinary case.
/// </param>
public sealed record ZoneRoute(uint AetheryteId, uint TerritoryTypeId, IReadOnlyList<uint> Gates)
{
    /// <summary>Whether getting there takes an aethernet hop as well.</summary>
    public bool Crosses => Gates.Count > 0;
}

/// <summary>Getting to the right zone.</summary>
public static class Aetherytes
{
    /// <summary>
    /// An attuned aetheryte in the given territory, or nothing if the player has
    /// not unlocked one.
    /// </summary>
    /// <remarks>
    /// Which aetheryte in the zone is not worth agonising over: vnavmesh walks
    /// whatever is left, and picking the closest one would mean resolving
    /// aetheryte map markers for a saving of a minute of running at most.
    /// </remarks>
    public static unsafe uint? AttunedIn(IDataManager data, uint territoryTypeId)
    {
        var sheet = data.GetExcelSheet<Aetheryte>();
        var telepo = Telepo.Instance();
        if (telepo == null)
            return null;

        telepo->UpdateAetheryteList();

        foreach (var entry in telepo->TeleportList)
        {
            if (!sheet.TryGetRow(entry.AetheryteId, out var aetheryte))
                continue;
            if (!aetheryte.IsAetheryte)
                continue;
            if (aetheryte.Territory.RowId != territoryTypeId)
                continue;

            return entry.AetheryteId;
        }

        return null;
    }

    /// <summary>
    /// How to get to a zone, or nothing when the player cannot get there at all.
    /// </summary>
    /// <remarks>
    /// Nearly every zone answers this with one of its own aetherytes. One does
    /// not: the Dravanian Hinterlands has no crystal standing in it, and the
    /// game's own sheets say so plainly, since the territory points at
    /// Idyllshire's aetheryte and Idyllshire is a territory of its own.
    ///
    /// The way in from there is the aethernet. Two of Idyllshire's shards, the
    /// Prologue and Epilogue Gates, are recorded as standing in the Hinterlands
    /// rather than in the town, so the hop out to one of them is the zone
    /// transition itself. Nothing has to be walked through and no waypoint has
    /// to be guessed at.
    ///
    /// Written against the sheets rather than against that one zone, so a zone
    /// laid out the same way in some future patch is already handled.
    /// </remarks>
    public static ZoneRoute? RouteTo(IDataManager data, uint territoryTypeId)
    {
        if (AttunedIn(data, territoryTypeId) is { } direct)
            return new ZoneRoute(direct, territoryTypeId, []);

        if (!data.GetExcelSheet<TerritoryType>().TryGetRow(territoryTypeId, out var territory))
            return null;

        var serving = territory.Aetheryte.RowId;
        if (serving == 0 || !IsAttuned(serving))
            return null;

        if (!data.GetExcelSheet<Aetheryte>().TryGetRow(serving, out var aetheryte))
            return null;

        var gates = GatesInto(data, aetheryte.AethernetGroup, territoryTypeId);
        return gates.Count == 0
            ? null
            : new ZoneRoute(serving, aetheryte.Territory.RowId, gates);
    }

    /// <summary>
    /// The shards of one aethernet that stand in the given zone rather than in
    /// the town the network belongs to.
    /// </summary>
    /// <remarks>
    /// Handed back last first. Every gate lands in the zone, so the order costs
    /// flight time and nothing else, and for the one zone this applies to the
    /// later gate is the better start: the western gate sits far enough out
    /// that only 73 of the 521 Hinterlands spawn points shipped with the plugin
    /// lie beyond it.
    /// </remarks>
    private static List<uint> GatesInto(IDataManager data, byte aethernetGroup, uint territoryTypeId)
    {
        var gates = new List<uint>();

        foreach (var row in data.GetExcelSheet<Aetheryte>())
        {
            if (row.IsAetheryte)
                continue;
            if (row.AethernetGroup != aethernetGroup)
                continue;
            if (row.Territory.RowId != territoryTypeId)
                continue;

            gates.Add(row.RowId);
        }

        gates.Reverse();
        return gates;
    }

    /// <summary>Whether the player has attuned to an aetheryte, by its row id.</summary>
    private static unsafe bool IsAttuned(uint aetheryteId)
    {
        var telepo = Telepo.Instance();
        if (telepo == null)
            return false;

        telepo->UpdateAetheryteList();

        foreach (var entry in telepo->TeleportList)
        {
            if (entry.AetheryteId == aetheryteId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The aetheryte set as the home point, and the territory it stands in.
    /// Nothing when no home point is set, which a fresh character can manage.
    /// </summary>
    public static unsafe (uint AetheryteId, uint TerritoryTypeId)? Home(IDataManager data)
    {
        var player = PlayerState.Instance();
        if (player == null)
            return null;

        var id = (uint)player->HomeAetheryteId;
        if (id == 0)
            return null;

        if (!data.GetExcelSheet<Aetheryte>().TryGetRow(id, out var aetheryte))
            return null;

        return (id, aetheryte.Territory.RowId);
    }

    public static unsafe bool Teleport(uint aetheryteId)
    {
        var telepo = Telepo.Instance();
        return telepo != null && telepo->Teleport(aetheryteId, 0);
    }
}
