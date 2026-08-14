using System.Numerics;
using AutoKill.Data;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace AutoKill.UI;

/// <summary>
/// Two ways into the same question: pick the mob, or pick what you want it to
/// drop and let the index work out which mob that is.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly Func<MobIndex?> index;

    private string mobQuery = string.Empty;
    private string itemQuery = string.Empty;
    private uint selectedItem;
    private string selectedItemName = string.Empty;
    private MobEntry? selectedMob;

    public MainWindow(Func<MobIndex?> index)
        : base("AutoKill###AutoKillMain")
    {
        this.index = index;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        var mobs = index();
        if (mobs is null)
        {
            ImGui.TextUnformatted("Loading mob data...");
            return;
        }

        using var tabs = ImRaii.TabBar("##autokill-tabs");
        if (!tabs)
            return;

        DrawMobTab(mobs);
        DrawDropTab(mobs);
    }

    private void DrawMobTab(MobIndex mobs)
    {
        using var tab = ImRaii.TabItem("By mob");
        if (!tab)
            return;

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##mob-query", "mob name", ref mobQuery, 64);

        using var child = ImRaii.Child("##mob-results", new Vector2(-1, -1));
        if (!child)
            return;

        foreach (var mob in mobs.SearchMobs(mobQuery))
        {
            var label = mob.Farmable
                ? $"{mob.Name}##{mob.BNpcNameId}"
                : $"{mob.Name} (no known location)##{mob.BNpcNameId}";

            if (ImGui.Selectable(label, selectedMob?.BNpcNameId == mob.BNpcNameId))
                selectedMob = mob;

            if (selectedMob?.BNpcNameId == mob.BNpcNameId)
                DrawMobDetail(mob);
        }
    }

    private void DrawDropTab(MobIndex mobs)
    {
        using var tab = ImRaii.TabItem("By drop");
        if (!tab)
            return;

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##item-query", "item name", ref itemQuery, 64);

        using var child = ImRaii.Child("##item-results", new Vector2(-1, -1));
        if (!child)
            return;

        if (selectedItem == 0)
        {
            foreach (var (itemId, name) in mobs.SearchItems(itemQuery))
            {
                if (!ImGui.Selectable($"{name}##item{itemId}"))
                    continue;
                selectedItem = itemId;
                selectedItemName = name;
            }

            return;
        }

        if (ImGui.Button("Back"))
        {
            selectedItem = 0;
            return;
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(selectedItemName);
        ImGui.Separator();

        var droppers = mobs.MobsDropping(selectedItem);
        if (droppers.Count == 0)
        {
            ImGui.TextUnformatted("Nothing known drops this.");
            return;
        }

        foreach (var mob in droppers)
        {
            ImGui.TextUnformatted(mob.Name);
            DrawMobDetail(mob);
        }
    }

    private static void DrawMobDetail(MobEntry mob)
    {
        using var indent = ImRaii.PushIndent();

        if (!mob.Farmable)
        {
            ImGui.TextDisabled("No recorded spawn positions.");
            return;
        }

        foreach (var location in mob.Locations.Take(5))
        {
            ImGui.TextDisabled(
                $"{location.ZoneName}  ({location.Position.X:F0}, {location.Position.Z:F0})  "
                + $"x{location.SpawnCount}");
        }

        if (mob.Locations.Count > 5)
            ImGui.TextDisabled($"... and {mob.Locations.Count - 5} more");
    }
}
