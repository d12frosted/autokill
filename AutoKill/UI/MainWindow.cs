using System.Numerics;
using AutoKill.Core;
using AutoKill.Data;
using AutoKill.Farming;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace AutoKill.UI;

/// <summary>
/// Two ways into the same question: pick the mob, or pick what you want it to
/// drop and let the index work out which mob that is. Either way it ends at a
/// farm spot with a Farm button.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly Func<MobIndex?> index;
    private readonly FarmController farming;

    private string mobQuery = string.Empty;
    private string itemQuery = string.Empty;
    private uint selectedItem;
    private string selectedItemName = string.Empty;
    private MobEntry? selectedMob;

    private int killTarget;
    private int itemTarget;
    private int minuteTarget;
    private bool requireAll;

    public MainWindow(Func<MobIndex?> index, FarmController farming)
        : base("AutoKill###AutoKillMain")
    {
        this.index = index;
        this.farming = farming;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 360),
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

        if (farming.Blocker is { } blocker)
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), blocker);

        DrawSession();
        DrawTargets();
        ImGui.Separator();

        using var tabs = ImRaii.TabBar("##autokill-tabs");
        if (!tabs)
            return;

        DrawMobTab(mobs);
        DrawDropTab(mobs);
    }

    private void DrawSession()
    {
        if (farming.Current is not { } session)
            return;

        var progress = session.Progress;
        ImGui.TextUnformatted($"{session.Mob.Name} in {session.Location.ZoneName}");
        ImGui.TextDisabled($"{session.Phase}: {session.Status}");
        ImGui.TextDisabled(
            $"kills {progress.Kills}   elapsed {progress.Elapsed:hh\\:mm\\:ss}"
            + string.Concat(progress.ItemsGained.Select(g => $"   item {g.Key} x{g.Value}")));

        if (farming.Running && ImGui.Button("Stop"))
            farming.Stop();

        ImGui.Separator();
    }

    private void DrawTargets()
    {
        ImGui.TextDisabled("Stop when");
        ImGui.SetNextItemWidth(120);
        ImGui.InputInt("kills##target", ref killTarget);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        ImGui.InputInt("minutes##target", ref minuteTarget);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        ImGui.InputInt("drops##target", ref itemTarget);

        if (selectedItem != 0 && itemTarget > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"of {selectedItemName}");
        }

        ImGui.Checkbox("meet every target, not just one", ref requireAll);
        if (killTarget <= 0 && minuteTarget <= 0 && itemTarget <= 0)
            ImGui.TextDisabled("nothing set, so it runs until you stop it");
    }

    private StopConditions BuildConditions(MobEntry mob)
    {
        var conditions = new List<IStopCondition>();
        if (killTarget > 0)
            conditions.Add(new KillCountCondition(killTarget));
        if (minuteTarget > 0)
            conditions.Add(new ElapsedCondition(TimeSpan.FromMinutes(minuteTarget)));
        if (itemTarget > 0)
        {
            // The item being searched for if there is one, otherwise whatever
            // this mob is known to drop.
            var itemId = selectedItem != 0 ? selectedItem : mob.Drops.FirstOrDefault();
            if (itemId != 0)
                conditions.Add(new ItemCountCondition(itemId, itemTarget));
        }

        // Never keep going after dying or filling the bags, whatever else is set.
        conditions.Add(new DeathCondition());
        conditions.Add(new InventoryFullCondition());

        return new StopConditions(conditions, requireAll ? StopMode.All : StopMode.Any);
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

    private void DrawMobDetail(MobEntry mob)
    {
        using var indent = ImRaii.PushIndent();

        if (!mob.Farmable)
        {
            ImGui.TextDisabled("No recorded spawn positions.");
            return;
        }

        foreach (var location in mob.Locations.Take(5))
        {
            using var id = ImRaii.PushId($"{mob.BNpcNameId}-{location.TerritoryTypeId}-{location.Position.X:F0}");

            if (ImGui.SmallButton("Farm"))
                farming.Start(mob, location, BuildConditions(mob));

            ImGui.SameLine();
            ImGui.TextDisabled(
                $"{location.ZoneName}  ({location.Position.X:F0}, {location.Position.Z:F0})  "
                + $"x{location.SpawnCount}");
        }

        if (mob.Locations.Count > 5)
            ImGui.TextDisabled($"... and {mob.Locations.Count - 5} more");
    }
}
