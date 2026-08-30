using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class CraftingListTests
{
    // A short chain, since the whole question is what happens below the top of
    // one: a hide is tanned into leather, leather and ingots make a saddle.
    private const uint IngotRecipe = 100;
    private const uint LeatherRecipe = 200;
    private const uint SaddleRecipe = 300;
    private const uint MissingRecipe = 999;

    private const uint IronOre = 10;
    private const uint Coke = 11;
    private const uint Ingot = 12;
    private const uint Hide = 13;
    private const uint Leather = 14;
    private const uint Saddle = 15;

    private static readonly CraftRecipe[] Recipes =
    [
        new(IngotRecipe, Ingot, 1, [new CraftIngredient(IronOre, 2), new CraftIngredient(Coke, 1)]),
        new(LeatherRecipe, Leather, 2, [new CraftIngredient(Hide, 3)]),
        new(SaddleRecipe, Saddle, 1, [new CraftIngredient(Leather, 2), new CraftIngredient(Ingot, 1)]),
    ];

    private static CraftRecipe? ById(uint recipeId) =>
        Recipes.FirstOrDefault(recipe => recipe.RecipeId == recipeId);

    private static CraftRecipe? ForItem(uint itemId) =>
        Recipes.FirstOrDefault(recipe => recipe.ItemId == itemId);

    private static IReadOnlyDictionary<uint, int> Materials(params CraftEntry[] entries) =>
        CraftingLists.Materials(entries, ById, ForItem);

    [Fact]
    public void MaterialsScaleWithTheNumberOfCrafts()
    {
        var materials = Materials(new CraftEntry(IngotRecipe, 5, false));

        Assert.Equal(10, materials[IronOre]);
        Assert.Equal(5, materials[Coke]);
    }

    [Fact]
    public void MaterialsAddUpAcrossEntries()
    {
        var materials = Materials(
            new CraftEntry(IngotRecipe, 5, false),
            new CraftEntry(LeatherRecipe, 3, false));

        Assert.Equal(10, materials[IronOre]);
        Assert.Equal(5, materials[Coke]);
        Assert.Equal(9, materials[Hide]);
    }

    /// <summary>
    /// The point of the whole thing: nothing a mob drops is ever the item on the
    /// list, it is two steps under it, so a subcraft the list does not mention
    /// still has to be followed down to what it is made of.
    /// </summary>
    [Fact]
    public void SubcraftsTheListDoesNotMentionAreFollowedDown()
    {
        var materials = Materials(new CraftEntry(SaddleRecipe, 1, false));

        Assert.Equal(2, materials[Leather]);
        Assert.Equal(1, materials[Ingot]);

        // One craft of leather yields two, so two leather is one craft, which is
        // three hides rather than six.
        Assert.Equal(3, materials[Hide]);
        Assert.Equal(2, materials[IronOre]);
    }

    [Fact]
    public void PartialCraftsRoundUpBecauseHalfACraftIsNotAThing()
    {
        // Three leather is two crafts of two, so six hides.
        var materials = Materials(new CraftEntry(SaddleRecipe, 3, false));

        Assert.Equal(6, materials[Leather]);
        Assert.Equal(9, materials[Hide]);
    }

    /// <summary>
    /// A subcraft with an entry of its own is left alone. Its entry contributes
    /// its ingredients already, and following it down from above as well would
    /// count them twice.
    /// </summary>
    [Fact]
    public void SubcraftsAlreadyOnTheListAreNotFollowedDown()
    {
        var materials = Materials(
            new CraftEntry(SaddleRecipe, 1, false),
            new CraftEntry(LeatherRecipe, 1, false));

        Assert.Equal(2, materials[Leather]);
        Assert.Equal(3, materials[Hide]);
    }

    [Fact]
    public void SkippedAndEmptyEntriesContributeNothing()
    {
        var materials = Materials(
            new CraftEntry(IngotRecipe, 5, true),
            new CraftEntry(LeatherRecipe, 0, false),
            new CraftEntry(IngotRecipe, -1, false));

        Assert.Empty(materials);
    }

    [Fact]
    public void UnknownRecipesAreIgnoredRatherThanThrowing()
    {
        var materials = Materials(
            new CraftEntry(MissingRecipe, 5, false),
            new CraftEntry(IngotRecipe, 1, false));

        Assert.Equal(2, materials[IronOre]);
    }

    [Fact]
    public void IngredientsWithNoItemOrNoAmountAreDropped()
    {
        var odd = new CraftRecipe(
            IngotRecipe, Ingot, 1,
            [new CraftIngredient(0, 3), new CraftIngredient(IronOre, 0), new CraftIngredient(Coke, 1)]);

        var materials = CraftingLists.Materials(
            [new CraftEntry(IngotRecipe, 1, false)], _ => odd, _ => null);

        Assert.Equal(new Dictionary<uint, int> { [Coke] = 1 }, materials);
    }

    /// <summary>
    /// Recipe data is community shaped and occasionally circular. A loop should
    /// come out as an odd number, not a stack overflow.
    /// </summary>
    [Fact]
    public void CircularRecipesTerminate()
    {
        var first = new CraftRecipe(1, 100, 1, [new CraftIngredient(200, 1)]);
        var second = new CraftRecipe(2, 200, 1, [new CraftIngredient(100, 1)]);

        var materials = CraftingLists.Materials(
            [new CraftEntry(1, 1, false)],
            id => id == 1 ? first : second,
            item => item == 100 ? first : second);

        Assert.True(materials[200] > 0);
    }

    [Fact]
    public void StillNeededIsWhatIsMissingRatherThanTheWholeRequirement()
    {
        Assert.Equal(20, CraftingLists.StillNeeded(30, 10));
        Assert.Equal(0, CraftingLists.StillNeeded(30, 30));
        Assert.Equal(0, CraftingLists.StillNeeded(30, 45));
    }

    /// <summary>
    /// A list nobody has put anything on is its own answer, and has to be told
    /// apart from a full list of things no mob carries. Both show no rows, and
    /// only one of them is somebody else's fault.
    /// </summary>
    [Fact]
    public void AnEmptyListIsNotTheSameAsAListWithNothingToFarm()
    {
        Assert.Equal(ListStanding.Empty, CraftingLists.Standing(0, 0, 0));
        Assert.Equal(ListStanding.NothingToFarm, CraftingLists.Standing(12, 0, 0));
    }

    [Fact]
    public void AListWhoseDropsAreAllInTheBagsIsGathered()
    {
        Assert.Equal(ListStanding.Gathered, CraftingLists.Standing(12, 3, 0));
    }

    [Fact]
    public void AListWithSomethingLeftIsWorthGoingOutFor()
    {
        Assert.Equal(ListStanding.Outstanding, CraftingLists.Standing(12, 3, 1));
    }
}
