using System.Numerics;
using AutoKill.Core;
using AutoKill.Data;
using AutoKill.Farming;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
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
    private readonly ArtisanLists artisan;
    private readonly HuntBills hunts;
    private readonly Fates fates;
    private readonly Action saveConfig;

    private string mobQuery = string.Empty;
    private string itemQuery = string.Empty;
    private uint selectedItem;
    private string selectedItemName = string.Empty;
    private int selectedItemWanted = 1;
    private MobEntry? selectedMob;

    private CraftingList? craftingList;
    private IReadOnlyList<ListMaterial> craftingMaterials = [];

    private FarmTarget? plannedTarget;
    private readonly Dictionary<uint, int> itemGoals = [];
    private int killTarget;
    private int minuteTarget;
    private bool requireAll;
    private bool resultDismissed;
    private bool adjusting;

    public MainWindow(
        Func<MobIndex?> index,
        FarmController farming,
        ITextureProvider textures,
        Configuration config,
        Observations observations,
        RunHistory history,
        ArtisanLists artisan,
        HuntBills hunts,
        Fates fates,
        Action saveConfig)
        : base("AutoKill###AutoKillMain")
    {
        this.index = index;
        this.farming = farming;
        this.textures = textures;
        this.config = config;
        this.observations = observations;
        this.history = history;
        this.artisan = artisan;
        this.hunts = hunts;
        this.fates = fates;
        this.saveConfig = saveConfig;
        SizeConstraints = new WindowSizeConstraints
        {
            // Wide enough that a row's name and its counts do not have to fight
            // for the same space, which is what makes a list readable at a
            // glance rather than word by word.
            MinimumSize = new Vector2(560, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        using var style = Style.Window();

        var mobs = index();
        if (mobs is null)
        {
            Style.Muffled("Loading mob data...");
            return;
        }

        if (farming.Blocker is { } blocker)
        {
            ImGui.TextColored(Style.Bad, blocker);
            ImGui.Separator();
        }

        var session = farming.Current;
        if (session is not null && !(session.Phase == FarmPhase.Finished && resultDismissed))
        {
            // The title carries the state too, since the window is often behind
            // something else while a run is going.
            WindowName = session.Phase == FarmPhase.Paused
                ? $"AutoKill - paused - {session.Target.Name}###AutoKillMain"
                : $"AutoKill - {session.Target.Name}###AutoKillMain";
            DrawRun(mobs, session);
            return;
        }

        WindowName = "AutoKill###AutoKillMain";

        if (plannedTarget is not null)
        {
            DrawPlan(mobs, plannedTarget);
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
        DrawHuntTab(mobs);
        DrawHistoryTab(mobs);
        DrawLearnedTab(mobs);
        DrawSettingsTab();
    }

    /// <summary>
    /// The hunt bills in hand, and what is left on each.
    /// </summary>
    /// <remarks>
    /// A bill already is what this window asks for: a mob, a zone and a number.
    /// The counts are the game's own, so they are right about kills from before
    /// the plugin was opened, and the goal offered is what is left rather than
    /// the whole bill.
    /// </remarks>
    private void DrawHuntTab(MobIndex mobs)
    {
        using var tab = ImRaii.TabItem("Hunts");
        if (!tab)
            return;

        var bills = hunts.Obtained();
        if (bills.Count == 0)
        {
            Style.Gap();
            Style.Muffled("No hunt bills in hand.");
            Style.Muffled("Pick some up from a hunt board and they turn up here.");
            return;
        }

        using var child = ImRaii.Child("##hunts", new Vector2(-1, -1));
        if (!child)
            return;

        foreach (var bill in bills)
        {
            Style.Place(bill.Name);
            if (bill.Elite)
                Style.Trailing("one mark, killed once");

            using var indent = ImRaii.PushIndent();

            foreach (var target in bill.Targets)
            {
                using var id = ImRaii.PushId($"{bill.Name}-{target.BNpcNameId}");
                DrawHuntTarget(mobs, target);
            }

            Style.Gap(2f);
        }
    }

    private void DrawHuntTarget(MobIndex mobs, HuntTarget target)
    {
        var where = string.IsNullOrEmpty(target.Where) ? target.Zone : $"{target.Where}, {target.Zone}";

        if (target.Done)
        {
            Style.Muffled(target.Name);
            ImGui.SameLine();
            ImGui.TextColored(Style.Good, "done");
            Style.Trailing($"{target.Needed} / {target.Needed}");
            return;
        }

        ImGui.TextUnformatted(target.Name);
        Style.Level(mobs.Get(target.BNpcNameId)?.Level);
        Style.Trailing($"{target.Killed} / {target.Needed}");

        using var indent = ImRaii.PushIndent();
        Style.Muffled(where);

        if (target.Fated)
        {
            DrawFatedTarget(mobs, target);
            return;
        }

        // Only the zone the bill names. A mob of the same name elsewhere counts
        // for nothing, and sending someone there would be worse than useless.
        var areas = mobs.Get(target.BNpcNameId)?.Areas
            .Where(area => area.TerritoryTypeId == target.TerritoryTypeId)
            .ToList() ?? [];

        if (areas.Count == 0)
        {
            Style.Muffled("nowhere recorded in that zone");
            return;
        }

        foreach (var area in areas.Take(2))
        {
            using var areaId = ImRaii.PushId($"{area.Centre.X:F0}-{area.Centre.Z:F0}");

            if (Style.Pick(
                    $"({area.MapCentre.X:F1}, {area.MapCentre.Y:F1})",
                    $"Go here for {target.Name}, {target.Remaining} still owed.",
                    level: area.Level))
            {
                PlanHunt(mobs, target, area);
            }

            Style.Trailing(Density(area));
        }

        if (areas.Count > 2)
            Style.Muffled($"and {areas.Count - 2} more in that zone");
    }

    /// <summary>
    /// A target that only exists while a FATE is running.
    /// </summary>
    /// <remarks>
    /// Standing where it would be is how a run waits forever, so this only
    /// offers to go when the FATE is actually up, and then goes to where the
    /// FATE is rather than where the mob was once recorded. That is the better
    /// position anyway: it is live rather than remembered.
    ///
    /// Being out of the zone is not the same as the FATE being down, and saying
    /// so would be a guess dressed as a fact.
    /// </remarks>
    private void DrawFatedTarget(MobIndex mobs, HuntTarget target)
    {
        var named = string.IsNullOrEmpty(target.FateName) ? "a FATE" : $"the FATE \"{target.FateName}\"";

        if (fates.Running(target.FateId) is { } running)
        {
            if (Style.Pick(
                    $"{named} is up now",
                    "Goes to where the FATE actually is, rather than where the mob was recorded."))
            {
                PlanHunt(mobs, target, mobs.AreaAt(target.TerritoryTypeId, running.Position));
            }

            Style.Trailing($"{running.Progress}% done");
            return;
        }

        Style.Muffled(fates.InZone(target.TerritoryTypeId)
            ? $"{named} is not running"
            : $"only while {named} is running");
    }

    private void PlanHunt(MobIndex mobs, HuntTarget target, FarmArea area)
    {
        if (mobs.Get(target.BNpcNameId) is not { } mob)
            return;

        // What is left, not what the bill asked for, and nothing else. A bill is
        // finished by killing, so a time limit or a drop would only get in the way.
        killTarget = target.Remaining;
        minuteTarget = 0;
        requireAll = false;

        // The bill names one mob, and only that one counts towards it, so this
        // is the one flow that never takes a whole field.
        Plan(new FarmTarget(mob, area), carryItem: false);
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
            Style.Gap();
            Style.Muffled("No runs yet.");
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

            Style.Place(run.Named);
            Style.Trailing($"{run.When:d MMM HH:mm}");

            using var indent = ImRaii.PushIndent();

            ImGui.TextUnformatted(mobs.ZoneName(run.TerritoryId));

            // Rates as well as totals, because "which field is better" is a
            // question about pace, and totals only answer it after arithmetic.
            Style.Trailing(Pace.PerHour(run.Kills, elapsed) is { } pace
                ? $"{run.Kills} killed in {elapsed:hh\\:mm\\:ss}   {pace:F0}/h"
                : $"{run.Kills} killed in {elapsed:hh\\:mm\\:ss}");
            Style.Muffled(run.Reason);

            foreach (var (itemId, count) in run.Gained)
            {
                DrawItemIcon(mobs, itemId);
                Style.Muffled($"{count} {mobs.ItemName(itemId)}");
                if (Pace.PerHour(count, elapsed) is { } perHour)
                    Style.Trailing($"{perHour:F0}/h");
            }

            if (Repeatable(mobs, run) is not null)
            {
                if (ImGui.SmallButton("repeat"))
                    Repeat(mobs, run);
            }
            else
            {
                Style.Muffled("that ground is no longer in the data");
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("forget"))
                history.Forget(run);

            Style.Gap(2f);
        }
    }

    /// <summary>The most recent finished run over this ground.</summary>
    private RunRecord? LastRunHere(FarmTarget target) =>
        history.Records.FirstOrDefault(run =>
            run.TerritoryId == target.Area.TerritoryTypeId
            && run.Mobs.Intersect(target.BNpcNameIds).Any());

    /// <summary>The ground this run covered, as the current data sees it.</summary>
    private static FarmTarget? Repeatable(MobIndex mobs, RunRecord run)
    {
        var killed = run.Mobs
            .Select(mobs.Get)
            .Where(mob => mob is not null)
            .Select(mob => mob!)
            .ToList();

        return mobs.Fields(killed)
            .Where(field => field.Area.TerritoryTypeId == run.TerritoryId)
            .OrderBy(field => Vector2.Distance(
                new Vector2(field.Area.Centre.X, field.Area.Centre.Z), new Vector2(run.AreaX, run.AreaZ)))
            .FirstOrDefault();
    }

    private void Repeat(MobIndex mobs, RunRecord run)
    {
        if (Repeatable(mobs, run) is not { } target)
            return;

        killTarget = run.KillTarget;
        minuteTarget = (int)Math.Round(run.MinuteTarget);
        requireAll = run.RequireAll;

        Plan(target);

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
            Style.Gap();
            Style.Muffled("Nothing learnt yet. Farm something and this fills in.");
            return;
        }

        ImGui.Spacing();
        if (ImGui.Button("Forget everything"))
            observations.ForgetEverything();

        ImGui.SameLine();
        Style.Trailing($"{entries.Count} place(s) farmed");
        ImGui.Separator();

        using var child = ImRaii.Child("##learned", new Vector2(-1, -1));
        if (!child)
            return;

        using var table = ImRaii.Table(
            "##learned-table", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp);
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
            ImGui.TextColored(Style.Accent, mobs.Get(mobId)?.Name ?? $"mob {mobId}");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(mobs.ZoneName(territoryId));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(what.Kills.ToString());

            ImGui.TableNextColumn();
            if (what.Typical() is { } typical)
            {
                ImGui.TextUnformatted($"{typical.Typical.TotalSeconds:F0}s");
                ImGui.SameLine();

                // What the number is, not just how much of it there is. One is
                // a respawn; the other is a respawn plus the trip back.
                Style.Muffled(typical.Timed
                    ? $"({typical.Samples} timed)"
                    : $"({typical.Samples} with the trip back)");
            }
            else
            {
                // Whichever kind is closest to standing on its own. Three of one
                // is an estimate; two of each is still two of each.
                var seen = Math.Max(what.Respawned.Count, what.Repopulated.Count);
                Style.Muffled($"not yet ({seen}/{Repopulation.Enough})");
            }

            ImGui.TableNextColumn();
            if (ImGui.SmallButton("forget"))
                observations.Forget(mobId, territoryId);
        }
    }

    private void DrawRun(MobIndex mobs, FarmSession session)
    {
        var progress = session.Progress;
        var finished = session.Phase == FarmPhase.Finished;
        var paused = session.Phase == FarmPhase.Paused;

        Style.Place(session.Target.Name);
        Style.Level(session.Area.Level);
        ImGui.TextUnformatted(Where(session.Area));
        Style.Trailing(Density(session.Area));

        Style.Gap(2f);

        // Paused is the one state the run wants somebody to notice, since it
        // does not end on its own.
        ImGui.TextColored(
            finished ? Style.Good : paused ? Style.Accent : Style.Muted,
            finished ? $"Finished: {session.Status}" : $"{session.Phase}: {session.Status}");

        Style.Gap();
        Style.Heading("Progress");

        var kills = session.Conditions.Conditions.OfType<KillCountCondition>().FirstOrDefault();
        var time = session.Conditions.Conditions.OfType<ElapsedCondition>().FirstOrDefault();

        if (kills is null)
        {
            ImGui.TextUnformatted("kills");
            Style.Trailing(progress.Kills.ToString());
        }
        else
        {
            Style.Progress(
                "kills", progress.Kills, kills.Target,
                Reads(progress.Kills, kills.Target, progress.Elapsed));
        }

        if (time is null)
        {
            ImGui.TextUnformatted("elapsed");
            Style.Trailing($"{progress.Elapsed:hh\\:mm\\:ss}");
        }
        else
        {
            Style.Progress(
                "elapsed",
                (int)progress.Elapsed.TotalSeconds,
                (int)time.Limit.TotalSeconds,
                $"{progress.Elapsed:hh\\:mm\\:ss} / {time.Limit:hh\\:mm\\:ss}");
        }

        var itemTargets = session.Conditions.Conditions
            .OfType<ItemCountCondition>()
            .ToDictionary(c => c.ItemId, c => c.Target);

        foreach (var itemId in itemTargets.Keys.Concat(progress.ItemsGained.Keys).Distinct())
        {
            var have = progress.CountOf(itemId);

            DrawItemIcon(mobs, itemId);
            if (itemTargets.TryGetValue(itemId, out var target))
            {
                Style.Progress(
                    mobs.ItemName(itemId), have, target, Reads(have, target, progress.Elapsed));
            }
            else
            {
                ImGui.TextUnformatted(mobs.ItemName(itemId));
                Style.Trailing($"{have}");
            }
        }

        Style.Gap();
        ImGui.Separator();
        Style.Gap(2f);

        if (!finished)
        {
            if (adjusting)
            {
                DrawAdjust(mobs, session);
                return;
            }

            if (paused)
            {
                if (ImGui.Button("Resume", new Vector2(120f, 0f)))
                    farming.Resume();
            }
            else if (ImGui.Button("Pause", new Vector2(120f, 0f)))
            {
                farming.Pause();
            }

            ImGui.SameLine();
            if (ImGui.Button("Stop", new Vector2(120f, 0f)))
                farming.Stop();

            ImGui.SameLine();
            if (ImGui.SmallButton("adjust targets"))
            {
                LoadTargets(session);
                adjusting = true;
            }

            Style.Explain("Move the stop line without stopping the run.");
            return;
        }

        adjusting = false;

        // Dying ends the run, but it should not cost the setup. What is left
        // is the same ask less what the run banked: kills made, items held and
        // time spent all stay made, held and spent.
        if (session.Died && session.Outcome is { } outcome)
        {
            if (ImGui.Button("Pick it back up", new Vector2(160f, 0f))
                && farming.Start(session.Target, session.Conditions.Remaining(outcome)))
            {
                resultDismissed = false;
                return;
            }

            ImGui.SameLine();
        }

        if (ImGui.Button("Done", new Vector2(120f, 0f)))
        {
            resultDismissed = true;
            StepBack();
        }

        ImGui.SameLine();
        if (!ImGui.Button("Farm this again"))
            return;

        resultDismissed = true;
        Plan(session.Target);
    }

    /// <summary>
    /// Land somewhere the finished run makes useful.
    /// </summary>
    /// <remarks>
    /// A run picked off a crafting list ends back on that list when the bags now
    /// hold enough of the material, with the counts fresh and the next thing to
    /// farm in view. Landing on the material's own fields, which is where the
    /// window would otherwise return, offers to farm the one thing that is done.
    /// A material still short stays selected, since its fields are still the
    /// business at hand. Judged by the bags rather than by what the run was
    /// asked for, because the list is fed by what is held, not by what one run
    /// set out to get.
    /// </remarks>
    private void StepBack()
    {
        if (craftingList is null || selectedItem == 0)
            return;

        var material = craftingMaterials.FirstOrDefault(m => m.ItemId == selectedItem);
        if (material is null)
            return;

        if (CraftingLists.StillNeeded(material.Required, Bags.CountOf(material.ItemId)) == 0)
            selectedItem = 0;
    }

    /// <summary>
    /// The stop line, editable mid-run. The same controls as the plan, because
    /// they are the same question, prefilled with what the run is aiming at
    /// now. Nothing changes until Apply: a run should never chase a target
    /// somebody is halfway through typing.
    /// </summary>
    private void DrawAdjust(MobIndex mobs, FarmSession session)
    {
        DrawStopWhen(mobs, session.Target);

        Style.Gap(2f);
        if (ImGui.Button("Apply", new Vector2(120f, 0f)))
        {
            session.Retarget(BuildConditions());
            adjusting = false;
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120f, 0f)))
            adjusting = false;
    }

    /// <summary>
    /// "12 / 30", with how much longer the rest should take at the pace shown
    /// so far. Nothing extra when there is no pace to go on: a made-up number
    /// would sit on the bar looking like a fact.
    /// </summary>
    private static string? Reads(int done, int target, TimeSpan elapsed) =>
        Pace.TimeToGo(done, target, elapsed) is { } toGo && toGo > TimeSpan.Zero
            ? $"{done} / {target}   ~{Pace.Roughly(toGo)}"
            : null;

    /// <summary>What the run is aiming at now, loaded into the editable fields.</summary>
    private void LoadTargets(FarmSession session)
    {
        var current = session.Conditions;

        killTarget = current.Conditions.OfType<KillCountCondition>().FirstOrDefault()?.Target ?? 0;
        minuteTarget = (int)Math.Round(
            current.Conditions.OfType<ElapsedCondition>().FirstOrDefault()?.Limit.TotalMinutes ?? 0);
        requireAll = current.Mode == StopMode.All;

        itemGoals.Clear();
        foreach (var condition in current.Conditions.OfType<ItemCountCondition>())
            itemGoals[condition.ItemId] = condition.Target;
    }

    private void DrawPlan(MobIndex mobs, FarmTarget target)
    {
        var area = target.Area;

        Style.Place(target.Name);
        Style.Level(area.Level);
        ImGui.TextUnformatted(Where(area));

        // Coordinates in a row are trusted blind; the map is how the game
        // answers "where is this exactly", so offer it before Start is pressed.
        ImGui.SameLine();
        if (ImGui.SmallButton("map"))
            farming.ShowOnMap(area);
        Style.Explain("Flag it on the map and open the map there.");

        Style.Trailing(Density(area));

        // What this ground actually gave last time, which is the one honest
        // basis for deciding whether it is worth going back.
        if (LastRunHere(target) is { } last)
        {
            var took = TimeSpan.FromSeconds(last.ElapsedSeconds);
            var what = string.Join(
                ", ", last.Gained.Select(g => $"{g.Value} {mobs.ItemName(g.Key)}"));

            Style.Muffled(what.Length > 0
                ? $"Last time here: {last.Kills} kills and {what}, in {Pace.Roughly(took)}."
                : $"Last time here: {last.Kills} kills in {Pace.Roughly(took)}.");
        }

        Style.Gap(2f);
        ImGui.Separator();
        DrawStopWhen(mobs, target);

        // On the plan rather than only in Settings, because whether coming home
        // is wanted depends on the run: it sticks as the default for the next
        // one, which is what a preference asked at the right moment does.
        var back = config.ReturnWhenDone;
        if (ImGui.Checkbox("teleport back here when it ends", ref back))
        {
            config.ReturnWhenDone = back;
            saveConfig();
        }

        Style.Explain(
            "Back to an aetheryte in this zone, once the run ends on its own. "
            + "A run you stop yourself stays where it is.");

        ImGui.Separator();
        Style.Gap(2f);

        // Said before the button rather than after it. A crafter or a job twenty
        // levels short ends the same way in both cases, and finding that out on
        // arrival is finding it out too late.
        var job = farming.Jobs.Plan(area.Level);
        if (job.Says is { } says)
        {
            ImGui.TextColored(job.Blocked ? Style.Bad : Style.Muted, says);
            Style.Gap(2f);
        }

        // The one thing on this screen worth pressing, coloured like it.
        using (ImRaii.Disabled(job.Blocked))
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Style.Accent with { W = 0.85f });
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Style.Accent);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.12f, 0.10f, 0.08f, 1f));
            var start = ImGui.Button("Start", new Vector2(120f, 0f));
            ImGui.PopStyleColor(3);

            if (start && farming.Start(target, BuildConditions()))
            {
                resultDismissed = false;
                ClearPlan();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Back"))
            ClearPlan();
    }

    /// <summary>
    /// The stop line: kills, minutes and items, and whether one or all of them
    /// ends the run. Shared between the plan and adjusting mid-run, so the two
    /// read as the same controls because they are.
    /// </summary>
    private void DrawStopWhen(MobIndex mobs, FarmTarget target)
    {
        Style.Heading("Stop when");

        ImGui.SetNextItemWidth(120);
        ImGui.InputInt("kills", ref killTarget);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        ImGui.InputInt("minutes", ref minuteTarget);

        if (target.Drops.Count > 0)
        {
            Style.Heading(target.Shared ? "Collect, from any of them" : "Collect");
            foreach (var itemId in target.Drops)
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
            Style.Muffled(target.Shared
                ? "Nothing known drops from any of these."
                : "Nothing known drops from this one.");
        }

        Style.Gap(2f);
        if (killTarget > 0 || minuteTarget > 0 || itemGoals.Count > 0)
            ImGui.Checkbox("meet every target, not just the first", ref requireAll);
        else
            Style.Muffled("no target set, so it will run until you stop it");
    }

    private void DrawSettingsTab()
    {
        using var tab = ImRaii.TabItem("Settings");
        if (!tab)
            return;

        Style.Heading("Needs");

        // Everything this leans on is somebody else's plugin, found at runtime.
        // A missing one produces silence rather than an error, so the state of
        // each is worth showing whether or not anything is wrong.
        foreach (var requirement in farming.Requirements.All())
        {
            var (mark, colour) = requirement.State switch
            {
                RequirementState.Good => ("ok", Style.Good),
                RequirementState.Blocking => ("!!", Style.Bad),
                _ => ("--", Style.Muted),
            };

            ImGui.TextColored(colour, mark);
            ImGui.SameLine();
            ImGui.TextUnformatted(requirement.Name);
            Style.Trailing(requirement.Detail);
        }

        Style.Gap();
        ImGui.Separator();
        Style.Heading("Getting about");

        var mountDistance = config.MountDistance;
        ImGui.SetNextItemWidth(220);
        if (ImGui.SliderFloat("mount with this much left to cover", ref mountDistance, 5f, 150f, "%.0f yalms"))
        {
            config.MountDistance = mountDistance;
            saveConfig();
        }

        Style.Muffled("Ground still to walk, with attack range already taken off,");
        Style.Muffled("so it means the same on a caster as on a melee job.");

        Style.Gap(2f);
        var patience = config.RespawnPatienceSeconds;
        ImGui.SetNextItemWidth(220);
        if (ImGui.SliderFloat("look a little longer before moving on", ref patience, 0f, 30f, "%.0f seconds"))
        {
            config.RespawnPatienceSeconds = patience;
            saveConfig();
        }

        Style.Muffled("Not a respawn wait: nothing comes back this quickly. It only stops");
        Style.Muffled("a moment with nothing in view from sending it somewhere else.");

        Style.Gap();
        ImGui.Separator();
        Style.Heading("Going as the wrong job");

        Style.Muffled("A crafter stands in the field doing nothing, and a battle job too");
        Style.Muffled("far down dies in it. Neither says why, so this is checked first.");
        Style.Gap(2f);

        foreach (var (policy, label, detail) in JobPolicies)
        {
            if (ImGui.RadioButton(label, config.JobPolicy == policy))
            {
                config.JobPolicy = policy;
                saveConfig();
            }

            Style.Explain(detail);

            // Indented under the choice it belongs to. Left until after all
            // three it reads as a detail of the last one, which is the single
            // option it has nothing to do with.
            if (policy != JobPolicy.Switch || config.JobPolicy != JobPolicy.Switch)
                continue;

            DrawPreferredJob();
            Style.Gap(2f);
        }

        Style.Gap();
        ImGui.Separator();
        Style.Heading("While it runs");

        var notifications = config.Notifications;
        if (ImGui.Checkbox("announce starts and finishes", ref notifications))
        {
            config.Notifications = notifications;
            saveConfig();
        }

        // The chat line and toast are silent, and a run ends precisely when
        // nobody is looking at the screen.
        var sound = config.FinishSound;
        ImGui.SetNextItemWidth(160);
        if (ImGui.SliderInt(
                "sound when a run ends", ref sound, 0, 16, sound == 0 ? "none" : $"<se.{sound}>"))
        {
            config.FinishSound = sound;
            saveConfig();
        }

        if (config.FinishSound > 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("play"))
                Notifier.Ring(config.FinishSound);
        }

        var companion = config.SummonCompanion;
        if (ImGui.Checkbox("keep the chocobo out", ref companion))
        {
            config.SummonCompanion = companion;
            saveConfig();
        }

        if (companion)
        {
            using var indent = ImRaii.PushIndent();

            var stance = (int)config.CompanionStance;
            ImGui.SetNextItemWidth(160);
            if (ImGui.Combo("stance", ref stance, "Free\0Defender\0Attacker\0Healer\0"))
            {
                config.CompanionStance = (ChocoboStance)(stance + (int)ChocoboStance.Free);
                saveConfig();
            }

            if (Companion.HasGreens())
                Style.Muffled($"out for another {Companion.TimeLeft / 60f:F0} min");
            else
                ImGui.TextColored(Style.Bad, "no Gysahl Greens, so it cannot be called");
        }

        Style.Gap();
        ImGui.Separator();
        Style.Heading("Afterwards");

        var returnWhenDone = config.ReturnWhenDone;
        if (ImGui.Checkbox("teleport back when a run ends", ref returnWhenDone))
        {
            config.ReturnWhenDone = returnWhenDone;
            saveConfig();
        }

        Style.Muffled("To an aetheryte in the zone the run set off from, once it ends");
        Style.Muffled("on its own. A run you stop yourself stays where it is.");

        Style.Gap(2f);
        var record = config.RecordRuns;
        if (ImGui.Checkbox("record runs to a trace file", ref record))
        {
            config.RecordRuns = record;
            saveConfig();
        }

        Style.Muffled("One file per run under the plugin's config folder,");
        Style.Muffled("recording where it went and what was standing nearby.");
    }

    /// <summary>
    /// Draw an item's icon inline, leaving the cursor where the text should go.
    /// Falls through silently when there is no icon, since a missing picture is
    /// not worth a gap in the row.
    /// </summary>
    private void DrawItemIcon(MobIndex mobs, uint itemId) => DrawIcon(mobs.ItemIcon(itemId));

    private void DrawIcon(ushort icon)
    {
        if (icon == 0)
            return;

        if (textures.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrDefault() is not { } texture)
            return;

        var size = ImGui.GetTextLineHeight() * 1.4f;
        ImGui.Image(texture.Handle, new Vector2(size, size));
        ImGui.SameLine();
    }

    private void Plan(FarmTarget target, bool carryItem = true)
    {
        plannedTarget = target;
        itemGoals.Clear();

        // Arriving from an item search means the item is already known, so do
        // not make it be picked out of the list a second time. How much is
        // wanted comes with it, which is the whole use of a crafting list.
        if (carryItem && selectedItem != 0 && target.Drops.Contains(selectedItem))
            itemGoals[selectedItem] = Math.Max(1, selectedItemWanted);
    }

    private void ClearPlan() => plannedTarget = null;

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
            using var id = ImRaii.PushId((int)mob.BNpcNameId);

            var open = selectedMob?.BNpcNameId == mob.BNpcNameId;

            // The whole span it is found at, since one name can cover a mob in
            // a starting zone and the same mob forty levels later. Each place
            // under it says which of those that ground is.
            if (Style.Named(mob.Name, mob.Level, open))
                selectedMob = open ? null : mob;

            Style.Trailing(mob.Farmable
                ? mob.Areas.Count == 1 ? "1 place" : $"{mob.Areas.Count} places"
                : "nowhere known");

            if (open)
                DrawMobDetail(mob);
        }
    }

    private void DrawDropTab(MobIndex mobs)
    {
        using var tab = ImRaii.TabItem("By drop");
        if (!tab)
            return;

        artisan.Refresh();
        DrawCraftingListPicker(mobs);

        if (craftingList is null)
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##item-query", "item name", ref itemQuery, 64);
        }

        using var child = ImRaii.Child("##item-results", new Vector2(-1, -1));
        if (!child)
            return;

        if (selectedItem == 0)
        {
            if (craftingList is not null)
            {
                DrawCraftingMaterials(mobs);
                return;
            }

            foreach (var (itemId, name) in mobs.SearchItems(itemQuery))
            {
                DrawItemIcon(mobs, itemId);
                if (!ImGui.Selectable($"{name}##item{itemId}"))
                    continue;
                Want(itemId, name, 1);
            }

            return;
        }

        if (ImGui.SmallButton("back"))
        {
            selectedItem = 0;
            return;
        }

        ImGui.SameLine();
        DrawItemIcon(mobs, selectedItem);
        ImGui.TextUnformatted(selectedItemName);
        if (selectedItemWanted > 1)
            Style.Trailing($"{selectedItemWanted} wanted");

        ImGui.Separator();
        Style.Gap(2f);

        var droppers = mobs.MobsDropping(selectedItem);
        if (droppers.Count == 0)
        {
            Style.Muffled("Nothing known drops this.");
            return;
        }

        DrawFields(mobs.FieldsDropping(selectedItem));

        // Said rather than left out. A mob with nowhere recorded is a gap in the
        // data, and silently dropping it reads as the mob not dropping the item.
        var nowhere = droppers.Where(mob => !mob.Farmable).ToList();
        if (nowhere.Count == 0)
            return;

        Style.Gap();
        Style.Muffled($"Also dropped by {Phrases.Kinds(nowhere.Select(mob => mob.Name).ToList())},");
        Style.Muffled("with nowhere recorded to find them.");
    }

    /// <summary>
    /// Somewhere to farm, and everything standing there that drops what was
    /// asked for.
    /// </summary>
    /// <remarks>
    /// Places rather than mobs, because the search was for an item and the item
    /// does not care which of them dropped it. Three kinds of petalouda share
    /// two fields in Elpis: offered one at a time, whichever is picked means
    /// flying past the other two. Offered as a field, the run kills all of them.
    ///
    /// One kind on its own is still worth having, so it stays available under
    /// each field rather than being decided for you.
    /// </remarks>
    private void DrawFields(IReadOnlyList<FarmTarget> fields)
    {
        foreach (var field in fields.Take(5))
        {
            var area = field.Area;
            using var id = ImRaii.PushId($"{area.TerritoryTypeId}-{area.Centre.X:F0}-{area.Centre.Z:F0}");

            // Where it is, then what is there. The zone carries the accent
            // because the zone is what is being chosen between.
            Style.Place(Where(area));
            Style.Trailing(Density(area));

            using var indent = ImRaii.PushIndent();

            // The row says what picking it does, so it needs no button beside
            // it saying "choose" as well.
            if (Style.Pick(
                    field.Name,
                    field.Shared
                        ? $"Go here and kill all {field.Mobs.Count} of them."
                        : "Go here and kill it.",
                    level: area.Level))
            {
                Plan(field);
            }

            if (field.Shared)
                DrawOnlyOne(field);

            Style.Gap(2f);
        }

        if (fields.Count > 5)
            Style.Muffled($"and {fields.Count - 5} more, further off");
    }

    /// <summary>
    /// The same field, one kind at a time, for when only one of them is wanted.
    /// </summary>
    /// <remarks>
    /// Named by whatever tells them apart rather than in full. Three controls
    /// reading "petalouda" would not be a choice, and the whole name is a hover
    /// away for anyone who wants to be sure.
    /// </remarks>
    private void DrawOnlyOne(FarmTarget field)
    {
        Style.Muffled("just one");

        var distinct = field.Distinct;

        for (var i = 0; i < field.Mobs.Count; i++)
        {
            var mob = field.Mobs[i];
            using var id = ImRaii.PushId((int)mob.BNpcNameId);

            if (Alone(mob, field.Area) is not { } own)
                continue;

            ImGui.SameLine();
            if (ImGui.SmallButton(distinct[i]))
                Plan(new FarmTarget(mob, own));

            Style.Explain(
                $"{mob.Name} on its own: {own.SpawnCount} spawns "
                + $"at ({own.MapCentre.X:F1}, {own.MapCentre.Y:F1})"
                + (own.Level is { } level ? $", {level}" : string.Empty));
        }
    }

    /// <summary>
    /// Which job to reach for when changing, out of the ones you have a gearset
    /// for.
    /// </summary>
    /// <remarks>
    /// Only the jobs you actually have kit for. Offering all twenty-odd would be
    /// offering to put you in a job with no gear, which the game will not do and
    /// nobody wants anyway.
    ///
    /// Left alone it picks whatever suits the field: something that kills things
    /// before something that survives them, and the highest of those. A named
    /// job wins over all of that, right up to the point where it cannot manage
    /// the field, and then it is passed over rather than sent there to die.
    /// </remarks>
    private void DrawPreferredJob()
    {
        using var indent = ImRaii.PushIndent();

        Style.Muffled("Only when a change is needed. A job already up to the field is left alone.");

        var choices = farming.Jobs.Choices();
        var preferred = choices.FirstOrDefault(job => job.ClassJobId == config.PreferredJob);

        // Named in front of the box rather than behind it. ImGui puts a label
        // after the control, and "go as" reads backwards when the answer to it
        // has already been given.
        ImGui.TextUnformatted("go as");
        ImGui.SameLine();

        ImGui.SetNextItemWidth(240);
        if (!ImGui.BeginCombo("##go-as", preferred is null ? Suits : $"{preferred.Name}   Lv{preferred.Level}"))
            return;

        // Not one of the jobs, so it does not sit among them looking like one.
        if (Style.Pick(
                Suits,
                "Something that kills things before something that survives them, highest first.",
                FontAwesomeIcon.Dice,
                config.PreferredJob == 0))
        {
            Prefer(0);
        }

        ImGui.Separator();

        foreach (var job in choices)
        {
            using var id = ImRaii.PushId((int)job.ClassJobId);

            if (Style.Pick(job.Name, null, FontAwesomeIcon.Khanda, job.ClassJobId == config.PreferredJob))
                Prefer(job.ClassJobId);

            Style.Trailing($"Lv{job.Level}");
        }

        ImGui.EndCombo();
    }

    private const string Suits = "whatever suits the field";

    private void Prefer(uint classJobId)
    {
        config.PreferredJob = classJobId;
        saveConfig();
    }

    /// <summary>
    /// What can be done about a job that cannot manage what was picked, in the
    /// order somebody would consider them.
    /// </summary>
    private static readonly (JobPolicy Policy, string Label, string Detail)[] JobPolicies =
    [
        (JobPolicy.Switch, "change job automatically",
            "Puts on a gearset that is up to the field, before the run starts."),
        (JobPolicy.Refuse, "refuse to start, and say why",
            "Nothing is changed for you. Change job yourself and the run will start."),
        (JobPolicy.Ignore, "go anyway",
            "Says what is wrong and starts regardless. Mobs a few levels up are killable."),
    ];

    private static string Where(FarmArea area) =>
        $"{area.ZoneName}   ({area.MapCentre.X:F1}, {area.MapCentre.Y:F1})";

    private static string Density(FarmArea area) =>
        area.Spots.Count > 1
            ? $"{area.SpawnCount} spawns   {area.Spots.Count} spots"
            : $"{area.SpawnCount} spawns";

    /// <summary>
    /// One mob's own patch of a shared field, for going after just that one.
    /// Its spots were folded in with everyone else's to make the field, so the
    /// area it would have had on its own is looked up again by where it is.
    /// </summary>
    private static FarmArea? Alone(MobEntry mob, FarmArea field) =>
        mob.Areas
            .Where(area => area.TerritoryTypeId == field.TerritoryTypeId)
            .OrderBy(area => Vector2.Distance(
                new Vector2(area.Centre.X, area.Centre.Z),
                new Vector2(field.Centre.X, field.Centre.Z)))
            .FirstOrDefault();

    /// <summary>
    /// Pick a crafting list instead of searching, when there are any.
    /// </summary>
    /// <remarks>
    /// A crafting list already says what has to be found and how much of it, so
    /// re-entering both by hand is work someone has already done. The lists come
    /// from Artisan, which is where they are kept.
    ///
    /// The rows say how much of each list is still worth going out for. Picking
    /// between lists otherwise means opening each one to find out whether it
    /// has anything left in it that a mob can supply.
    /// </remarks>
    private void DrawCraftingListPicker(MobIndex mobs)
    {
        if (!artisan.Installed || artisan.Lists.Count == 0)
            return;

        var lists = artisan.Lists;

        // Lists are re-read whenever Artisan saves them, so the one being shown
        // has to be found again each time rather than held on to. Editing a list
        // in the other window changes what is needed here without throwing away
        // whatever is being looked at.
        if (craftingList is not null)
        {
            var fresh = lists.FirstOrDefault(list => list.Id == craftingList.Id);
            if (fresh is null)
                ChooseCraftingList(null);
            else if (!ReferenceEquals(fresh, craftingList))
                Reread(fresh);
        }

        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo("##artisan-list", craftingList?.Name ?? "search for an item"))
            return;

        // Searching is not one of the lists, so it does not sit among them
        // looking like one.
        if (Style.Pick(
                "search for an item",
                "Look something up by name instead.",
                FontAwesomeIcon.Search,
                craftingList is null))
        {
            ChooseCraftingList(null);
        }

        ImGui.Separator();

        foreach (var list in lists)
        {
            using var id = ImRaii.PushId(list.Id);

            if (Style.Pick(list.Name, null, FontAwesomeIcon.ListUl, list.Id == craftingList?.Id))
                ChooseCraftingList(list);

            var (left, colour) = Outstanding(mobs, list);
            Style.Trailing(left, colour);
        }

        ImGui.EndCombo();
    }

    /// <summary>
    /// How much of a list is still worth going out for: materials a mob can
    /// supply and the bags do not already hold enough of.
    /// </summary>
    private (string Says, Vector4 Colour) Outstanding(MobIndex mobs, CraftingList list)
    {
        var farmable = artisan.Materials(list)
            .Where(material => !material.Crystal && mobs.AnythingDrops(material.ItemId))
            .ToList();

        if (farmable.Count == 0)
            return ("nothing a mob drops", Style.Muted);

        var left = farmable.Count(material =>
            CraftingLists.StillNeeded(material.Required, Bags.CountOf(material.ItemId)) > 0);

        return left == 0
            ? ("all gathered", Style.Good)
            : ($"{left} to farm", Style.Accent);
    }

    private void ChooseCraftingList(CraftingList? list)
    {
        Reread(list);
        selectedItem = 0;
    }

    private void Reread(CraftingList? list)
    {
        craftingList = list;
        craftingMaterials = list is null ? [] : artisan.Materials(list);
    }

    /// <summary>
    /// What a crafting list needs and a mob can supply, with how much of it is
    /// still to find.
    /// </summary>
    /// <remarks>
    /// Only the materials something drops are offered, since the rest are not
    /// this plugin's business, but how many were left out is worth saying: a
    /// list showing two rows out of thirty otherwise reads as broken.
    /// </remarks>
    private void DrawCraftingMaterials(MobIndex mobs)
    {
        var farmable = craftingMaterials
            .Where(material => !material.Crystal && mobs.AnythingDrops(material.ItemId))
            .ToList();

        var rest = craftingMaterials.Count - farmable.Count;

        if (farmable.Count == 0)
        {
            Style.Gap();
            Style.Muffled("Nothing on this list is worth farming a mob for.");
            if (rest > 0)
                Style.Muffled($"Its {rest} material(s) are gathered, bought or crafted.");
            return;
        }

        foreach (var material in farmable)
        {
            using var id = ImRaii.PushId((int)material.ItemId);

            var held = Bags.CountOf(material.ItemId);
            var missing = CraftingLists.StillNeeded(material.Required, held);
            var enough = missing == 0;

            DrawIcon(material.Icon);

            // What is done with steps back: dimmed, and marked so the state is
            // seen before the numbers are read. What is still owed keeps full
            // strength and says how much in the colour of the thing to act on.
            if (enough)
                ImGui.PushStyleColor(ImGuiCol.Text, Style.Muted);

            if (ImGui.Selectable($"{material.Name}##material"))
                Want(material.ItemId, material.Name, Math.Max(1, missing));

            if (enough)
                ImGui.PopStyleColor();

            Style.Explain(enough
                ? "You have enough of these already."
                : $"Look for {missing} more.");

            if (enough)
                Style.Trailing(FontAwesomeIcon.Check, $"{held} / {material.Required}", Style.Good);
            else
                Style.Trailing($"{held} / {material.Required}   {missing} to go", Style.Accent);
        }

        if (rest > 0)
        {
            Style.Gap();
            Style.Muffled($"{rest} other material(s) are gathered, bought or crafted.");
        }
    }

    /// <summary>Choose an item to go looking for, and how much of it.</summary>
    private void Want(uint itemId, string name, int quantity)
    {
        selectedItem = itemId;
        selectedItemName = name;
        selectedItemWanted = quantity;
    }

    private void DrawMobDetail(MobEntry mob)
    {
        using var indent = ImRaii.PushIndent();

        if (!mob.Farmable)
        {
            Style.Muffled("nowhere recorded to find it");
            return;
        }

        foreach (var area in mob.Areas.Take(5))
        {
            using var id = ImRaii.PushId($"{mob.BNpcNameId}-{area.TerritoryTypeId}-{area.Centre.X:F0}");

            if (Style.Pick(Where(area), $"Go here and kill {mob.Name}.", level: area.Level))
                Plan(new FarmTarget(mob, area));

            Style.Trailing(Density(area));
        }

        if (mob.Areas.Count > 5)
            Style.Muffled($"and {mob.Areas.Count - 5} more, further off");

        Style.Gap(2f);
    }
}
