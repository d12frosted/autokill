using System.Text.Json;
using System.Text.Json.Serialization;
using AutoKill.Core;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace AutoKill.Data;

/// <summary>A crafting list as Artisan saved it.</summary>
public sealed record CraftingList(int Id, string Name, IReadOnlyList<CraftEntry> Entries);

/// <summary>Something a list asks you to bring, with enough to show a row for it.</summary>
/// <param name="Crystal">
/// Shards, crystals and clusters. Mobs do drop them, and every list wants
/// hundreds, so without this they sit at the top of every list and bury the one
/// row worth farming. Nobody has ever farmed a mob for a crystal.
/// </param>
public sealed record ListMaterial(uint ItemId, string Name, ushort Icon, int Required, bool Crystal);

/// <summary>
/// Crafting lists read out of Artisan.
/// </summary>
/// <remarks>
/// Artisan keeps its lists in its own configuration file, which sits beside this
/// plugin's own. Reading that file is the whole of it: no IPC, no reaching into
/// a running plugin, and nothing that stops working when Artisan is not loaded.
///
/// The cost is that the file only changes when Artisan saves it, so a list being
/// edited right now reads as it was last saved. Artisan saves eagerly, and the
/// file is re-read whenever it changes on disk, so in practice this settles
/// within a moment of finishing an edit.
/// </remarks>
public sealed class ArtisanLists(string path, IDataManager data, IPluginLog log)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // Stopping the file being stat'd on every frame the tab is drawn.
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(1);

    /// <summary>The ItemUICategory shards, crystals and clusters all share.</summary>
    private const uint Crystals = 59;

    private DateTime lastChecked = DateTime.MinValue;
    private DateTime lastWritten = DateTime.MinValue;
    private Dictionary<uint, uint>? byResult;

    public IReadOnlyList<CraftingList> Lists { get; private set; } = [];

    /// <summary>Whether Artisan has ever been set up on this character's install.</summary>
    public bool Installed { get; private set; }

    /// <summary>
    /// Re-read the lists if the file has changed since last time. Cheap enough
    /// to call while drawing, and the only thing that keeps the picker honest
    /// while someone edits a list in the other window.
    /// </summary>
    public void Refresh()
    {
        var now = DateTime.UtcNow;
        if (now - lastChecked < CheckEvery)
            return;

        lastChecked = now;

        var file = new FileInfo(path);
        Installed = file.Exists;
        if (!Installed)
        {
            Lists = [];
            return;
        }

        if (file.LastWriteTimeUtc == lastWritten)
            return;

        lastWritten = file.LastWriteTimeUtc;
        Lists = Read();
    }

    private IReadOnlyList<CraftingList> Read()
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var config = JsonSerializer.Deserialize<ArtisanConfig>(stream, Json);

            return (config?.NewCraftingLists ?? [])
                .Where(list => !string.IsNullOrWhiteSpace(list.Name))
                .Select(list => new CraftingList(
                    list.Id,
                    list.Name!,
                    (list.Recipes ?? [])
                        .Select(item => new CraftEntry(
                            (uint)Math.Max(0, item.Id),
                            item.Quantity,
                            item.ListItemOptions?.Skipping ?? false))
                        .ToList()))
                .OrderBy(list => list.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            // A half-written file during someone else's save is the usual reason,
            // and the next check picks it up. Nothing here is worth interrupting.
            log.Warning(ex, "Could not read Artisan's crafting lists.");
            return [];
        }
    }

    /// <summary>Everything a list asks you to bring, most of it first.</summary>
    public IReadOnlyList<ListMaterial> Materials(CraftingList list)
    {
        var items = data.GetExcelSheet<Item>();

        return CraftingLists.Materials(list.Entries, RecipeById, RecipeForItem)
            .Select(pair => items.TryGetRow(pair.Key, out var item)
                ? new ListMaterial(
                    pair.Key,
                    item.Name.ExtractText(),
                    item.Icon,
                    pair.Value,
                    item.ItemUICategory.RowId == Crystals)
                : new ListMaterial(pair.Key, $"item {pair.Key}", 0, pair.Value, false))
            .OrderByDescending(material => material.Required)
            .ThenBy(material => material.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private CraftRecipe? RecipeById(uint recipeId) =>
        data.GetExcelSheet<Recipe>().TryGetRow(recipeId, out var recipe) ? Convert(recipe) : null;

    private CraftRecipe? RecipeForItem(uint itemId) =>
        ByResult().TryGetValue(itemId, out var recipeId) ? RecipeById(recipeId) : null;

    /// <summary>
    /// Which recipe makes which item, built once. Every subcraft followed down
    /// asks this question, and the recipe sheet is tens of thousands of rows.
    /// </summary>
    private Dictionary<uint, uint> ByResult()
    {
        if (byResult is not null)
            return byResult;

        byResult = [];
        foreach (var recipe in data.GetExcelSheet<Recipe>())
        {
            var result = recipe.ItemResult.RowId;

            // The same item is often made by several jobs. Any of them has the
            // same ingredients, so the first one found will do.
            if (result != 0)
                byResult.TryAdd(result, recipe.RowId);
        }

        return byResult;
    }

    private static CraftRecipe Convert(Recipe recipe)
    {
        var count = Math.Min(recipe.Ingredient.Count, recipe.AmountIngredient.Count);
        var ingredients = new List<CraftIngredient>(count);
        for (var i = 0; i < count; i++)
            ingredients.Add(new CraftIngredient(recipe.Ingredient[i].RowId, recipe.AmountIngredient[i]));

        return new CraftRecipe(recipe.RowId, recipe.ItemResult.RowId, recipe.AmountResult, ingredients);
    }

    // Only the handful of fields that matter. Artisan writes a great deal more,
    // including type names for its own serialiser, and all of it is ignored.
    private sealed class ArtisanConfig
    {
        public List<ArtisanList>? NewCraftingLists { get; set; }
    }

    private sealed class ArtisanList
    {
        [JsonPropertyName("ID")] public int Id { get; set; }

        public string? Name { get; set; }

        public List<ArtisanListItem>? Recipes { get; set; }
    }

    private sealed class ArtisanListItem
    {
        /// <summary>A recipe row, not an item row.</summary>
        [JsonPropertyName("ID")] public int Id { get; set; }

        /// <summary>Number of crafts.</summary>
        public int Quantity { get; set; }

        public ArtisanListItemOptions? ListItemOptions { get; set; }
    }

    private sealed class ArtisanListItemOptions
    {
        public bool Skipping { get; set; }
    }
}
