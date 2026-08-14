using System.Numerics;
using AutoKill.Core;
using AutoKill.Data;
using AutoKill.Farming;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace AutoKill.UI;

/// <summary>
/// Search, then plan, then run. Choosing a spot stages a plan rather than
/// starting one: a run that begins before its goal is set is a run whose goal
/// arrives too late to mean anything.
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

    private MobEntry? plannedMob;
    private FarmLocation? plannedLocation;
    private readonly Dictionary<uint, int> itemGoals = [];
    private int killTarget;
    private int minuteTarget;
    private bool requireAll;

    public MainWindow(Func<MobIndex?> index, FarmController farming)
        : base("AutoKill###AutoKillMain")
    {
        this.index = index;
        this.farming = farming;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 400),
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

        if (farming.Running)
            DrawSession(mobs);
        else if (plannedMob is not null && plannedLocation is not null)
            DrawPlan(mobs, plannedMob, plannedLocation);

        using var tabs = ImRaii.TabBar("##autokill-tabs");
        if (!tabs)
            return;

        DrawMobTab(mobs);
        DrawDropTab(mobs);
    }

    private void DrawSession(MobIndex mobs)
    {
        if (farming.Current is not { } session)
            return;

        var progress = session.Progress;
        ImGui.TextUnformatted($"{session.Mob.Name} in {session.Location.ZoneName}");
        ImGui.TextDisabled($"{session.Phase}: {session.Status}");
        ImGui.TextDisabled($"kills {progress.Kills}   elapsed {progress.Elapsed:hh\\:mm\\:ss}");

        foreach (var (itemId, count) in progress.ItemsGained)
            ImGui.TextDisabled($"{mobs.ItemName(itemId)} x{count}");

        if (ImGui.Button("Stop"))
            farming.Stop();

        ImGui.Separator();
    }

    private void DrawPlan(MobIndex mobs, MobEntry mob, FarmLocation location)
    {
        ImGui.TextUnformatted($"Farm {mob.Name}");
        ImGui.TextDisabled(
            $"{location.ZoneName}  ({location.Position.X:F0}, {location.Position.Z:F0})  "
            + $"x{location.SpawnCount}");

        ImGui.Spacing();
        ImGui.TextDisabled("Stop when");

        ImGui.SetNextItemWidth(120);
        ImGui.InputInt("kills", ref killTarget);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        ImGui.InputInt("minutes", ref minuteTarget);

        if (mob.Drops.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Collect");
            foreach (var itemId in mob.Drops)
            {
                using var id = ImRaii.PushId((int)itemId);

                var wanted = itemGoals.ContainsKey(itemId);
                if (ImGui.Checkbox($"{mobs.ItemName(itemId)}##want", ref wanted))
                {
                    if (wanted)
                        itemGoals[itemId] = 1;
                    else
                        itemGoals.Remove(itemId);
                }

                if (!wanted)
                    continue;

                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                var quantity = itemGoals[itemId];
                if (ImGui.InputInt("##quantity", ref quantity))
                    itemGoals[itemId] = Math.Max(1, quantity);
            }
        }
        else
        {
            ImGui.TextDisabled("Nothing known drops from this one.");
        }

        ImGui.Spacing();
        if (killTarget > 0 || minuteTarget > 0 || itemGoals.Count > 0)
            ImGui.Checkbox("meet every target, not just the first", ref requireAll);
        else
            ImGui.TextDisabled("no target set, so it will run until you stop it");

        if (ImGui.Button("Start"))
        {
            farming.Start(mob, location, BuildConditions());
            ClearPlan();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ClearPlan();

        ImGui.Separator();
    }

    private void Plan(MobEntry mob, FarmLocation location)
    {
        plannedMob = mob;
        plannedLocation = location;
        itemGoals.Clear();

        // Arriving from an item search means the item is already known, so do
        // not make it be picked out of the list a second time.
        if (selectedItem != 0 && mob.Drops.Contains(selectedItem))
            itemGoals[selectedItem] = 1;
    }

    private void ClearPlan()
    {
        plannedMob = null;
        plannedLocation = null;
    }

    private StopConditions BuildConditions()
    {
        var conditions = new List<IStopCondition>();
        if (killTarget > 0)
            conditions.Add(new KillCountCondition(killTarget));
        if (minuteTarget > 0)
            conditions.Add(new ElapsedCondition(TimeSpan.FromMinutes(minuteTarget)));
        foreach (var (itemId, quantity) in itemGoals)
            conditions.Add(new ItemCountCondition(itemId, quantity));

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

            if (ImGui.SmallButton("Choose"))
                Plan(mob, location);

            ImGui.SameLine();
            ImGui.TextDisabled(
                $"{location.ZoneName}  ({location.Position.X:F0}, {location.Position.Z:F0})  "
                + $"x{location.SpawnCount}");
        }

        if (mob.Locations.Count > 5)
            ImGui.TextDisabled($"... and {mob.Locations.Count - 5} more");
    }
}
