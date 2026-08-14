using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoKill.Farming;

/// <summary>What is in the bags right now.</summary>
/// <remarks>
/// Asked both during a run, to see whether a drop target has been met, and while
/// planning one, to see how much of something is still wanted.
/// </remarks>
public static class Bags
{
    public static unsafe int CountOf(uint itemId)
    {
        var manager = InventoryManager.Instance();
        return manager == null ? 0 : manager->GetInventoryItemCount(itemId);
    }

    public static unsafe bool IsFull()
    {
        var manager = InventoryManager.Instance();
        if (manager == null)
            return false;

        // The four ordinary bags. Anything else is not somewhere loot lands.
        for (var type = InventoryType.Inventory1; type <= InventoryType.Inventory4; type++)
        {
            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded)
                continue;

            for (var slot = 0; slot < container->Size; slot++)
            {
                if (container->GetInventorySlot(slot)->ItemId == 0)
                    return false;
            }
        }

        return true;
    }
}
