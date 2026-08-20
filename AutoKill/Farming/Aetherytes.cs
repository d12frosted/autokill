using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace AutoKill.Farming;

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
