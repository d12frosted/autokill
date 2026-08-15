using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace AutoKill.Data;

/// <summary>One mob a bill wants killed, and how far along it is.</summary>
/// <param name="FateId">
/// Some targets only exist while a FATE is running: the named ones on ordinary
/// bills are often the boss of one. Sending a run to stand where it would be is
/// how you wait forever, so the bill has to say this out loud.
/// </param>
/// <param name="Where">
/// The bill names a place inside the zone, like "Boulder Downs" rather than
/// "Coerthas Central Highlands". Nothing here can turn that into coordinates,
/// but it is worth showing: it is how anyone picks the right corner of a zone.
/// </param>
public sealed record HuntTarget(
    uint BNpcNameId,
    string Name,
    uint TerritoryTypeId,
    string Zone,
    string Where,
    ushort FateId,
    string FateName,
    int Needed,
    int Killed)
{
    public bool Fated => FateId != 0;

    public int Remaining => Math.Max(0, Needed - Killed);

    public bool Done => Remaining == 0;
}

/// <param name="Elite">
/// The weekly bill. One named mark, killed once, rather than three of something
/// ordinary.
/// </param>
public sealed record HuntBill(string Name, bool Elite, IReadOnlyList<HuntTarget> Targets)
{
    public bool Done => Targets.All(target => target.Done);
}

/// <summary>
/// The hunt bills currently in hand, and what is left on each.
/// </summary>
/// <remarks>
/// A bill is already a farming order: kill three of this, in that place. All of
/// it is in the client, so unlike everything else this plugin knows, none of it
/// is guesswork. The kill counts are the game's own, which means they are right
/// even about kills this plugin had nothing to do with.
///
/// Elite bills are included. Every one of them names a B rank, which is a mob a
/// single player is expected to kill: there are no A or S ranks on a bill. They
/// are also the best covered thing here, since a mark stands in a handful of
/// known places rather than wherever a field happens to spread.
/// </remarks>
public sealed class HuntBills(IDataManager data, IPluginLog log)
{
    /// <summary>The weekly bill, naming one mark instead of five targets.</summary>
    private const byte EliteBill = 2;

    public unsafe IReadOnlyList<HuntBill> Obtained()
    {
        var state = UIState.Instance();
        if (state == null)
            return [];

        var types = data.GetExcelSheet<MobHuntOrderType>();
        var orders = data.GetSubrowExcelSheet<MobHuntOrder>();
        var names = data.GetExcelSheet<BNpcName>();
        var territories = data.GetExcelSheet<TerritoryType>();

        var bills = new List<HuntBill>();

        for (byte markIndex = 0; markIndex < MobHunt.MaxMarkIndex; markIndex++)
        {
            if (!state->MobHunt.IsMarkBillObtained(markIndex))
                continue;

            if (!types.TryGetRow(markIndex, out var type))
                continue;

            var rowId = state->MobHunt.GetObtainedHuntOrderRowId(markIndex);

            // Each bill type owns a stretch of rows. A row outside it means the
            // read went wrong, and inventing targets from it would send someone
            // to the wrong end of the world.
            if (rowId < type.OrderStart.RowId || rowId >= type.OrderStart.RowId + type.OrderAmount)
            {
                log.Warning($"Hunt bill {markIndex} points at order {rowId}, which is not one of its own.");
                continue;
            }

            if (!orders.TryGetRow((uint)rowId, out var subrows))
                continue;

            var targets = new List<HuntTarget>();
            for (var mobIndex = 0; mobIndex < subrows.Count; mobIndex++)
            {
                var order = subrows[mobIndex];
                if (order.Target.ValueNullable is not { } target)
                    continue;

                var nameId = target.Name.RowId;
                if (nameId == 0 || !names.TryGetRow(nameId, out var name))
                    continue;

                var territoryId = target.Map.ValueNullable?.TerritoryType.RowId ?? 0;
                var zone = territories.GetRowOrDefault(territoryId)?.PlaceName.ValueNullable?.Name.ExtractText()
                           ?? string.Empty;

                var fate = target.FATE.ValueNullable;

                targets.Add(new HuntTarget(
                    nameId,
                    name.Singular.ExtractText(),
                    territoryId,
                    zone,
                    target.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty,
                    (ushort)target.FATE.RowId,
                    fate?.Name.ExtractText() ?? string.Empty,
                    order.NeededKills,
                    state->MobHunt.CurrentKills[markIndex][mobIndex]));
            }

            if (targets.Count > 0)
                bills.Add(new HuntBill(BillName(type), type.Type == EliteBill, targets));
        }

        return bills;
    }

    /// <summary>
    /// What the bill is called, which is the name of the paper it came on.
    /// </summary>
    private static string BillName(MobHuntOrderType type) =>
        type.EventItem.ValueNullable?.Name.ExtractText() is { Length: > 0 } name
            ? name
            : "Mark Bills";
}
