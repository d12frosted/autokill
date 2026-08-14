namespace AutoKill.Core;

/// <summary>One thing on a crafting list: a recipe and how many times to run it.</summary>
/// <param name="Crafts">
/// Number of crafts, not number of items. A recipe yielding three per craft and
/// asked for twice needs its ingredients twice, and hands back six.
/// </param>
public readonly record struct CraftEntry(uint RecipeId, int Crafts, bool Skipped);

public readonly record struct CraftIngredient(uint ItemId, int Amount);

/// <param name="Yield">How many items one craft produces.</param>
public sealed record CraftRecipe(
    uint RecipeId,
    uint ItemId,
    int Yield,
    IReadOnlyList<CraftIngredient> Ingredients);

/// <summary>
/// What a crafting list asks you to bring.
/// </summary>
/// <remarks>
/// Subcrafts are followed down, and that is the whole point. Nothing a mob drops
/// is ever the item on a crafting list: it is a hide two steps under it, tanned
/// into leather and then sewn into the thing being made. Counting only the direct
/// ingredients of what is on the list, which is what Artisan's own material panel
/// shows, would find nothing worth farming for most lists.
///
/// A subcraft with an entry of its own on the list is left alone, since that
/// entry contributes its ingredients already and following it down from above as
/// well would count them twice.
///
/// Intermediates are reported alongside what they are made of. Both are true
/// statements of what has to be obtained, and only one of them is ever a mob
/// drop, so nothing is lost by listing both and letting the caller pick.
/// </remarks>
public static class CraftingLists
{
    /// <summary>
    /// Deep enough for any real chain, and a backstop for data that disagrees
    /// with itself about what is made from what.
    /// </summary>
    private const int MaxDepth = 8;

    public static IReadOnlyDictionary<uint, int> Materials(
        IEnumerable<CraftEntry> entries,
        Func<uint, CraftRecipe?> recipeById,
        Func<uint, CraftRecipe?> recipeForItem)
    {
        var usable = entries
            .Where(entry => !entry.Skipped && entry.Crafts > 0)
            .Select(entry => (Entry: entry, Recipe: recipeById(entry.RecipeId)))
            .Where(pair => pair.Recipe is not null)
            .ToList();

        var onList = usable.Select(pair => pair.Recipe!.ItemId).ToHashSet();
        var totals = new Dictionary<uint, int>();

        foreach (var (entry, recipe) in usable)
            Add(recipe!, entry.Crafts, 0, []);

        return totals;

        void Add(CraftRecipe recipe, int times, int depth, HashSet<uint> making)
        {
            foreach (var ingredient in recipe.Ingredients)
            {
                if (ingredient.ItemId == 0 || ingredient.Amount <= 0)
                    continue;

                var needed = ingredient.Amount * times;
                totals[ingredient.ItemId] = totals.GetValueOrDefault(ingredient.ItemId) + needed;

                if (depth >= MaxDepth || onList.Contains(ingredient.ItemId))
                    continue;

                if (recipeForItem(ingredient.ItemId) is not { } sub || !making.Add(sub.RecipeId))
                    continue;

                var crafts = (int)Math.Ceiling(needed / (double)Math.Max(1, sub.Yield));
                Add(sub, crafts, depth + 1, making);
                making.Remove(sub.RecipeId);
            }
        }
    }

    /// <summary>
    /// How much of a material is still to be found. What is already in the bags
    /// counts, so a list is a target to reach rather than an amount to gather
    /// again from nothing.
    /// </summary>
    public static int StillNeeded(int required, int held) => Math.Max(0, required - held);
}
