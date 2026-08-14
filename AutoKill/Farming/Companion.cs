using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace AutoKill.Farming;

/// <summary>Which way the chocobo should fight.</summary>
public enum ChocoboStance
{
    Free = 4,
    Defender = 5,
    Attacker = 6,
    Healer = 7,
}

/// <summary>The chocobo companion: summoning it and keeping it useful.</summary>
/// <remarks>
/// Worth having on a long grind for the obvious reason that it fights, and for
/// a less obvious one: a companion earns its own experience while it is out, so
/// hours of farming that would otherwise teach it nothing do.
///
/// Companion commands are BuddyAction rows rather than general actions, and the
/// stance currently set is readable from the same place, so a stance is only
/// pressed when it is actually wrong.
/// </remarks>
public static class Companion
{
    private const uint GysahlGreens = 4868;

    // BuddyAction rows: 2 withdraw, 3 follow, 4 free, 5 defender, 6 attacker, 7 healer.
    private const uint Withdraw = 2;

    public static unsafe bool IsOut
    {
        get
        {
            var state = UIState.Instance();
            return state != null && state->Buddy.CompanionInfo.TimeLeft > 0;
        }
    }

    /// <summary>Seconds before the chocobo wanders off.</summary>
    public static unsafe float TimeLeft
    {
        get
        {
            var state = UIState.Instance();
            return state == null ? 0f : state->Buddy.CompanionInfo.TimeLeft;
        }
    }

    public static unsafe bool HasGreens()
    {
        var inventory = InventoryManager.Instance();
        return inventory != null && inventory->GetInventoryItemCount(GysahlGreens) > 0;
    }

    /// <summary>
    /// Summon the chocobo, or top up its timer. The same item does both, so
    /// there is nothing to decide between.
    /// </summary>
    public static unsafe bool Summon(ICondition condition)
    {
        if (condition[ConditionFlag.InCombat] || condition[ConditionFlag.Mounted])
            return false;
        if (!HasGreens())
            return false;

        var manager = ActionManager.Instance();
        return manager != null
               && manager->GetActionStatus(ActionType.Item, GysahlGreens) == 0
               && manager->UseAction(ActionType.Item, GysahlGreens);
    }

    public static unsafe bool SetStance(ChocoboStance stance)
    {
        var state = UIState.Instance();
        if (state == null || state->Buddy.CompanionInfo.TimeLeft <= 0)
            return false;

        // Already standing the right way, so pressing it again would only put it
        // back to the same place.
        if (state->Buddy.CompanionInfo.ActiveCommand == (uint)stance)
            return false;

        var manager = ActionManager.Instance();
        return manager != null && manager->UseAction(ActionType.BuddyAction, (uint)stance);
    }

    public static unsafe bool Dismiss()
    {
        var manager = ActionManager.Instance();
        return manager != null && manager->UseAction(ActionType.BuddyAction, Withdraw);
    }
}
