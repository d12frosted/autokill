using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace AutoKill.Farming;

/// <summary>Small things the character does on the way to a fight.</summary>
public static class PlayerActions
{
    // GeneralAction rows 9 and 24. Roulettes rather than a named mount, so this
    // works for anyone without asking which mounts they own, and the flying one
    // where flight is possible.
    private const uint MountRoulette = 9;
    private const uint FlyingMountRoulette = 24;

    public static bool IsMounted(ICondition condition) =>
        condition[ConditionFlag.Mounted] || condition[ConditionFlag.RidingPillion];

    /// <summary>
    /// In the air on a flying mount. Ground paths cannot be followed from up
    /// here, and dismounting means falling out of the sky.
    /// </summary>
    public static bool IsFlying(ICondition condition) => condition[ConditionFlag.InFlight];

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

    /// <summary>
    /// Summon a mount, preferring the flying roulette where flight is possible
    /// so the thing summoned is one that can actually leave the ground.
    /// </summary>
    public static unsafe bool Mount(bool preferFlying)
    {
        var manager = ActionManager.Instance();
        if (manager == null)
            return false;

        if (preferFlying && manager->GetActionStatus(ActionType.GeneralAction, FlyingMountRoulette) == 0)
            return manager->UseAction(ActionType.GeneralAction, FlyingMountRoulette);

        return manager->UseAction(ActionType.GeneralAction, MountRoulette);
    }

    /// <summary>
    /// Get off the mount, which takes two presses from the air.
    /// </summary>
    /// <remarks>
    /// This is the mount action itself rather than the dismount general action,
    /// and pressing it while flying only ends the flight. A second press once
    /// back on the ground is what actually dismounts, so callers press it until
    /// the character is neither flying nor mounted rather than pressing once and
    /// assuming it worked.
    ///
    /// A press is refused outright while jumping, which is worth knowing given
    /// what leaving the ground involves.
    /// </remarks>
    public static unsafe bool Dismount(ICondition condition)
    {
        if (condition[ConditionFlag.Jumping] || condition[ConditionFlag.Jumping61])
            return false;

        var manager = ActionManager.Instance();
        return manager != null && manager->UseAction(ActionType.Mount, 0);
    }

    /// <summary>
    /// Whether this zone can be flown in yet.
    /// </summary>
    /// <remarks>
    /// A zone with no aether current set cannot be flown in by anyone, and one
    /// whose currents are unfinished cannot be flown in by this character.
    /// Taking off is left to vnavmesh, which jumps on its own once the path
    /// climbs and the character is mounted, so the only thing worth knowing
    /// here is whether to ask for a path through the air at all.
    /// </remarks>
    public static unsafe bool CanFlyIn(IDataManager data, uint territoryTypeId)
    {
        if (!data.GetExcelSheet<TerritoryType>().TryGetRow(territoryTypeId, out var territory))
            return false;

        var flagSet = territory.AetherCurrentCompFlgSet.RowId;
        if (flagSet == 0)
            return false;

        var state = PlayerState.Instance();
        return state != null && state->IsAetherCurrentZoneComplete(flagSet);
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
