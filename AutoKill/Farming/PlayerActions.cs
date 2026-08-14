using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace AutoKill.Farming;

/// <summary>Small things the character does on the way to a fight.</summary>
public static class PlayerActions
{
    // GeneralAction row 9. Roulette rather than a named mount, so it works for
    // anyone without asking which mounts they own.
    private const uint MountRoulette = 9;

    // GeneralAction row 23. Nothing can be cast from the saddle, so the mount
    // that made travel quick has to go before a single blow can be struck.
    private const uint DismountAction = 23;

    public static bool IsMounted(ICondition condition) =>
        condition[ConditionFlag.Mounted] || condition[ConditionFlag.RidingPillion];

    /// <summary>True while the mount is being summoned, so nothing should move.</summary>
    public static bool IsMounting(ICondition condition) =>
        condition[ConditionFlag.Mounting]
        || condition[ConditionFlag.Mounting71]
        || condition[ConditionFlag.MountOrOrnamentTransition];

    /// <summary>
    /// Whether mounting is worth trying at all. GetActionStatus already knows
    /// about zones that forbid mounts, being in combat and everything else that
    /// would make the attempt fail, so there is no point second guessing it.
    /// </summary>
    public static unsafe bool CanMount(ICondition condition)
    {
        if (IsMounted(condition) || IsMounting(condition))
            return false;
        if (condition[ConditionFlag.InCombat] || condition[ConditionFlag.Casting])
            return false;

        var manager = ActionManager.Instance();
        return manager != null && manager->GetActionStatus(ActionType.GeneralAction, MountRoulette) == 0;
    }

    public static unsafe bool Mount()
    {
        var manager = ActionManager.Instance();
        return manager != null && manager->UseAction(ActionType.GeneralAction, MountRoulette);
    }

    public static unsafe bool Dismount()
    {
        var manager = ActionManager.Instance();
        return manager != null && manager->UseAction(ActionType.GeneralAction, DismountAction);
    }

    /// <summary>
    /// Put a flag on the map where the run is heading, so it is obvious at a
    /// glance where the character is going and why.
    /// </summary>
    public static unsafe void FlagDestination(IDataManager data, uint territoryTypeId, Vector3 position)
    {
        var agent = AgentMap.Instance();
        if (agent == null)
            return;

        if (!data.GetExcelSheet<TerritoryType>().TryGetRow(territoryTypeId, out var territory))
            return;

        var mapId = territory.Map.RowId;
        if (mapId == 0)
            return;

        agent->SetFlagMapMarker(territoryTypeId, mapId, position);
    }
}
