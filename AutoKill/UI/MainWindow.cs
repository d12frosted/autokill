using System.Numerics;
using AutoKill.Core;
using AutoKill.Data;
using AutoKill.Farming;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace AutoKill.UI;

/// <summary>
/// One window, four states, and only ever one of them on screen.
/// </summary>
/// <remarks>
/// Browsing, planning and running are separate jobs sharing no controls.
/// Showing them together invites searching during a run and setting goals for a
/// mob already left behind, so the window becomes whichever one is in hand:
///
///   browse  search by mob or by drop, or change settings
///   plan    the chosen area, and what would end the run
///   run     what is happening and how far along it is
///   done    what happened, until dismissed
/// </remarks>
public sealed class MainWindow : Window
{
    private readonly Func<MobIndex?> index;
    private readonly FarmController farming;
    private readonly ITextureProvider textures;
    private readonly Configuration config;
    private readonly Observations observations;
    private readonly RunHistory history;
    private readonly Action saveConfig;

    private string mobQuery = string.Empty;
    private string itemQuery = string.Empty;
    private uint selectedItem;
    private string selectedItemName = string.Empty;
    private MobEntry? selectedMob;

    private MobEntry? plannedMob;
    private FarmArea? plannedArea;
    private readonly Dictionary<uint, int> itemGoals = [];
    private int killTarget;
    private int minuteTarget;
    private bool requireAll;
    private bool resultDismissed;

    public MainWindow(
        Func<MobIndex?> index,
        FarmController farming,
        ITextureProvider textures,
        Configuration config,
        Observations observations,
        RunHistory history,
        Action saveConfig)
        : base("AutoKill###AutoKillMain")
    {
        this.index = index;
        this.farming = farming;
        this.textures = textures;
        this.config = config;
        this.observations = observations;
        this.history = history;
        this.saveConfig = saveConfig;
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
        {
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), blocker);
            ImGui.Separator();
        }

        var session = farming.Current;
        if (session is not null && !(session.Phase == FarmPhase.Finished && resultDismissed))
        {
            // The title carries the state too, since the window is often behind
            // something else while a run is going.
            WindowName = $"AutoKill - {session.Mob.Name}###AutoKillMain";
            DrawRun(mobs, session);
            return;
        }

        WindowName = "AutoKill###AutoKillMain";

        if (plannedMob is not null && plannedArea is not null)
        {
            DrawPlan(mobs, plannedMob, plannedArea);
            return;
        }

        DrawBrowse(mobs);
    }

    private void DrawBrowse(MobIndex mobs)
    {
        using var tabs = ImRaii.TabBar("##autokill-tabs");
        if (!tabs)
            return;

        DrawMobTab(mobs);
        DrawDropTab(mobs);
        DrawHistoryTab(mobs);
        DrawLearnedTab(mobs);
        DrawSettingsTab();
    }

    /// <summary>
    /// Finished runs, and a way to do one again.
    /// </summary>
    /// <remarks>
    /// Repeating looks the area up afresh from the mob, the zone and roughly
    /// where it was, rather than from a stored copy. Areas come from data that
    /// changes between versions, and a saved one would keep sending runs to
    /// spots that may no longer be there.
    /// </remarks>
    private void DrawHistoryTab(MobIndex mobs)
    {
        using var tab = ImRaii.TabItem("History");
        if (!tab)
            return;

        if (history.Records.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("No runs yet.");
            return;
        }

        ImGui.Spacing();
        if (ImGui.Button("Clear history"))
            history.ForgetEverything();

        ImGui.Separator();

        using var child = ImRaii.Child("##history", new Vector2(-1, -1));
        if (!child)
            return;

        foreach (var run in history.Records.ToList())
        {
            using var id = ImRaii.PushId(run.When.Ticks.GetHashCode());

            var elapsed = TimeSpan.FromSeconds(run.ElapsedSeconds);
            ImGui.TextUnformatted($"{run.MobName}  in {mobs.ZoneName(run.TerritoryId)}");
            ImGui.TextDisabled(
                $"{run.When:d MMM HH:mm}   {run.Kills} killed in {elapsed:hh\\:mm\\:ss}   {run.Reason}");

            foreach (var (itemId, count) in run.Gained)
            {
                DrawItemIcon(mobs, itemId);
                ImGui.TextDisabled($"{count} {mobs.ItemName(itemId)}");
            }

            if (Repeatable(mobs, run) is not null)
            {
                if (ImGui.SmallButton("Repeat"))
                    Repeat(mobs, run);
            }
            else
            {
                ImGui.TextDisabled("that area is no longer in the data");
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Forget"))
                history.Forget(run);

            ImGui.Separator();
        }
    }

    /// <summary>The area this run used, as the current data sees it.</summary>
    private static FarmArea? Repeatable(MobIndex mobs, RunRecord run) =>
        mobs.Get(run.MobId)?.Areas
            .Where(a => a.TerritoryTypeId == run.TerritoryId)
            .OrderBy(a => Vector2.Distance(
                new Vector2(a.Centre.X, a.Centre.Z), new Vector2(run.AreaX, run.AreaZ)))
            .FirstOrDefault();

    private void Repeat(MobIndex mobs, RunRecord run)
    {
        if (mobs.Get(run.MobId) is not { } mob || Repeatable(mobs, run) is not { } area)
            return;

        killTarget = run.KillTarget;
        minuteTarget = (int)Math.Round(run.MinuteTarget);
        requireAll = run.RequireAll;

        Plan(mob, area);

        // Plan clears the goals and may preselect a searched item, so the
        // remembered ones go in afterwards or they would be thrown away.
        itemGoals.Clear();
        foreach (var (itemId, count) in run.ItemTargets)
            itemGoals[itemId] = count;
    }

    /// <summary>
    /// What farming has taught the plugin, and a way to throw it away.
    /// </summary>
    /// <remarks>
    /// Learnt data changes how a run behaves, so it should be possible to see it
    /// and be rid of it. A zone reworked in a patch, or a stretch of bad luck
    /// recorded as slow respawns, is worth forgetting rather than living with.
    /// </remarks>
    private void DrawLearnedTab(MobIndex mobs)
    {
        using var tab = ImRaii.TabItem("Learned");
        if (!tab)
            return;

        var entries = observations.Entries.ToList();
        if (entries.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Nothing learnt yet. Farm something and this fills in.");
            return;
        }

        ImGui.Spacing();
        if (ImGui.Button("Forget everything"))
            observations.ForgetEverything();

        ImGui.SameLine();
        ImGui.TextDisabled($"{entries.Count} place(s) farmed");
        ImGui.Separator();

        using var child = ImRaii.Child("##learned", new Vector2(-1, -1));
        if (!child)
            return;

        using var table = ImRaii.Table("##learned-table", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("mob");
        ImGui.TableSetupColumn("zone");
        ImGui.TableSetupColumn("kills");
        ImGui.TableSetupColumn("comes back in");
        ImGui.TableSetupColumn(string.Empty);
        ImGui.TableHeadersRow();

        foreach (var (mobId, territoryId, what) in entries)
        {
            using var id = ImRaii.PushId($"{mobId}-{territoryId}");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(mobs.Get(mobId)?.Name ?? $"mob {mobId}");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(mobs.ZoneName(territoryId));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(what.Kills.ToString());

            ImGui.TableNextColumn();
            if (what.TypicalRepopulation() is { } typical)
                ImGui.TextUnformatted($"{typical.TotalSeconds:F0}s  ({what.Repopulated.Count} seen)");
            else
                ImGui.TextDisabled($"not yet ({what.Repopulated.Count}/3)");

            ImGui.TableNextColumn();
            if (ImGui.SmallButton("Forget"))
                observations.Forget(mobId, territoryId);
        }
    }

    private void DrawRun(MobIndex mobs, FarmSession session)
    {
        var progress = session.Progress;
        var finished = session.Phase == FarmPhase.Finished;

        ImGui.TextUnformatted(session.Mob.Name);
        ImGui.TextDisabled(
            $"{session.Area.ZoneName}  ({session.Area.MapCentre.X:F1}, {session.Area.MapCentre.Y:F1})"
            + (session.Area.Spots.Count > 1 ? $"  {session.Area.Spots.Count} spots" : string.Empty));

        ImGui.Spacing();
        ImGui.TextUnformatted(finished
            ? $"Finished: {session.Status}"
            : $"{session.Phase}: {session.Status}");
        ImGui.Spacing();

        var kills = session.Conditions.Conditions.OfType<KillCountCondition>().FirstOrDefault();
        var time = session.Conditions.Conditions.OfType<ElapsedCondition>().FirstOrDefault();

        ImGui.TextUnformatted(kills is null
            ? $"kills {progress.Kills}"
            : $"kills {progress.Kills}/{kills.Target}");
        ImGui.TextUnformatted(time is null
            ? $"elapsed {progress.Elapsed:hh\\:mm\\:ss}"
            : $"elapsed {progress.Elapsed:hh\\:mm\\:ss}/{time.Limit:hh\\:mm\\:ss}");

        var itemTargets = session.Conditions.Conditions
            .OfType<ItemCountCondition>()
            .ToDictionary(c => c.ItemId, c => c.Target);

        foreach (var itemId in itemTargets.Keys.Concat(progress.ItemsGained.Keys).Distinct())
        {
            var have = progress.CountOf(itemId);
            DrawItemIcon(mobs, itemId);
            ImGui.TextUnformatted(itemTargets.TryGetValue(itemId, out var target)
                ? $"{mobs.ItemName(itemId)} {have}/{target}"
                : $"{mobs.ItemName(itemId)} x{have}");
        }

        ImGui.Spacing();
        ImGui.Separator();

        if (!finished)
        {
            if (ImGui.Button("Stop"))
                farming.Stop();
            return;
        }

        if (ImGui.Button("Done"))
            resultDismissed = true;

        ImGui.SameLine();
        if (!ImGui.Button("Farm this again"))
            return;

        resultDismissed = true;
        Plan(session.Mob, session.Area);
    }

    private void DrawPlan(MobIndex mobs, MobEntry mob, FarmArea area)
    {
        ImGui.TextUnformatted($"Farm {mob.Name}");
        ImGui.TextDisabled(
            $"{area.ZoneName}  ({area.MapCentre.X:F1}, {area.MapCentre.Y:F1})  "
            + $"{area.SpawnCount} spawns"
            + (area.Spots.Count > 1 ? $" across {area.Spots.Count} spots" : string.Empty));

        ImGui.Spacing();
        ImGui.Separator();
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

                DrawItemIcon(mobs, itemId);
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

        ImGui.Separator();

        if (ImGui.Button("Start"))
        {
            resultDismissed = false;
            farming.Start(mob, area, BuildConditions());
            ClearPlan();
        }

        ImGui.SameLine();
        if (ImGui.Button("Back"))
            ClearPlan();
    }

    private void DrawSettingsTab()
    {
        using var tab = ImRaii.TabItem("Settings");
        if (!tab)
            return;

        ImGui.Spacing();

        var mountDistance = config.MountDistance;
        ImGui.SetNextItemWidth(220);
        if (ImGui.SliderFloat("mount and fly beyond", ref mountDistance, 5f, 150f, "%.0f yalms"))
        {
            config.MountDistance = mountDistance;
            saveConfig();
        }

        var patience = config.RespawnPatienceSeconds;
        ImGui.SetNextItemWidth(220);
        if (ImGui.SliderFloat("wait at an empty spot", ref patience, 0f, 60f, "%.0f seconds"))
        {
            config.RespawnPatienceSeconds = patience;
            saveConfig();
        }

        var notifications = config.Notifications;
        if (ImGui.Checkbox("announce starts and finishes", ref notifications))
        {
            config.Notifications = notifications;
            saveConfig();
        }

        ImGui.Spacing();
        ImGui.Separator();

        var record = config.RecordRuns;
        if (ImGui.Checkbox("record runs to a trace file", ref record))
        {
            config.RecordRuns = record;
            saveConfig();
        }

        ImGui.TextDisabled("One file per run under the plugin's config folder,");
        ImGui.TextDisabled("recording where it went and what was standing nearby.");
    }

    /// <summary>
    /// Draw an item's icon inline, leaving the cursor where the text should go.
    /// Falls through silently when there is no icon, since a missing picture is
    /// not worth a gap in the row.
    /// </summary>
    private void DrawItemIcon(MobIndex mobs, uint itemId)
    {
        var icon = mobs.ItemIcon(itemId);
        if (icon == 0)
            return;

        if (textures.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrDefault() is not { } texture)
            return;

        var size = ImGui.GetTextLineHeight() * 1.4f;
        ImGui.Image(texture.Handle, new Vector2(size, size));
        ImGui.SameLine();
    }

    private void Plan(MobEntry mob, FarmArea area)
    {
        plannedMob = mob;
        plannedArea = area;
        itemGoals.Clear();

        // Arriving from an item search means the item is already known, so do
        // not make it be picked out of the list a second time.
        if (selectedItem != 0 && mob.Drops.Contains(selectedItem))
            itemGoals[selectedItem] = 1;
    }

    private void ClearPlan()
    {
        plannedMob = null;
        plannedArea = null;
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
                DrawItemIcon(mobs, itemId);
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
        DrawItemIcon(mobs, selectedItem);
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

        foreach (var area in mob.Areas.Take(5))
        {
            using var id = ImRaii.PushId($"{mob.BNpcNameId}-{area.TerritoryTypeId}-{area.Centre.X:F0}");

            if (ImGui.SmallButton("Choose"))
                Plan(mob, area);

            ImGui.SameLine();
            ImGui.TextDisabled(
                $"{area.ZoneName}  ({area.MapCentre.X:F1}, {area.MapCentre.Y:F1})  "
                + $"{area.SpawnCount} spawns"
                + (area.Spots.Count > 1 ? $" over {area.Spots.Count} spots" : string.Empty));
        }

        if (mob.Areas.Count > 5)
            ImGui.TextDisabled($"... and {mob.Areas.Count - 5} more");
    }
}
