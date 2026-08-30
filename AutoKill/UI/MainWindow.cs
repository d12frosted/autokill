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
    private readonly IPlayerState player;
    private readonly ITextureProvider textures;
    private readonly Configuration config;
    private readonly Observations observations;
    private readonly RunHistory history;
    private readonly ArtisanLists artisan;
    private readonly HuntBills hunts;
    private readonly Func<HuntingLog?> logbook;
    private readonly Fates fates;
    private readonly PastRuns past;
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

    // Browsing while a run grinds is planning the next one, which is exactly
    // what a twenty minute farm is for. Only the goals of the running one stay
    // out of reach.
    private bool browsingMeanwhile;

    // The run this window knows about, and whether it has made way for the
    // overlay carrying it.
    private FarmSession? knownSession;
    private bool steppedAside;

    private IDisposable? shell;

    public MainWindow(
        Func<MobIndex?> index,
        FarmController farming,
        IPlayerState player,
        ITextureProvider textures,
        Configuration config,
        Observations observations,
        RunHistory history,
        ArtisanLists artisan,
        HuntBills hunts,
        Func<HuntingLog?> logbook,
        Fates fates,
        PastRuns past,
        Action saveConfig)
        : base("AutoKill###AutoKillMain")
    {
        this.index = index;
        this.farming = farming;
        this.player = player;
        this.textures = textures;
        this.config = config;
        this.observations = observations;
        this.history = history;
        this.artisan = artisan;
        this.hunts = hunts;
        this.logbook = logbook;
        this.fates = fates;
        this.past = past;
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

    /// <summary>
    /// Getting out of the way of the overlay, and coming back afterwards.
    /// </summary>
    /// <remarks>
    /// Run every frame whether or not the window is open, which is the whole
    /// point: a window that has stepped aside cannot decide for itself when to
    /// come back.
    ///
    /// Two windows saying the same thing is one too many, so while the overlay
    /// carries the run this one makes way, and the cog on the overlay brings it
    /// back to the tabs rather than to a copy of what the overlay already
    /// shows. It returns on its own when the run ends, because a result is
    /// something to read rather than something to watch, and this is the window
    /// with room for it.
    /// </remarks>
    public override void PreOpenCheck()
    {
        var session = farming.Current;

        // A new run is a new result to show, whatever was dismissed before it.
        if (!ReferenceEquals(knownSession, session))
        {
            knownSession = session;
            resultDismissed = false;

            // Only step aside if there is something to step aside from. A
            // window that was already closed has made way for nothing, and
            // bringing it back at the end of a run nobody was watching would
            // be a surprise rather than a courtesy.
            if (config.ShowOverlay && session is { Phase: not FarmPhase.Finished } && IsOpen)
            {
                steppedAside = true;
                browsingMeanwhile = true;
                IsOpen = false;
            }
        }

        // Not cleared when one stop of a list hands over to the next: the
        // window stepped aside for the list, so it is the end of the list that
        // brings it back.
        if (steppedAside && session is { Phase: FarmPhase.Finished })
        {
            steppedAside = false;
            browsingMeanwhile = false;
            IsOpen = true;
        }
    }

    /// <summary>
    /// The shell goes around the frame rather than inside it, so the title bar
    /// and chrome are part of the signature too.
    /// </summary>
    public override void PreDraw() => shell = Style.Shell();

    public override void PostDraw()
    {
        shell?.Dispose();
        shell = null;
    }

    public override void Draw()
    {
        // Whose window this is, and whose bags and gearsets every number below
        // assumes. The masthead is the anchor every screen hangs from.
        Style.Masthead("AutoKill", Whose());

        var mobs = index();
        if (mobs is null)
        {
            Style.Nothing("Nothing to show yet. The mob list is still being read.");
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
            // A finish always brings the result forward, wherever the browsing
            // had wandered: it is the one thing worth interrupting for.
            if (session.Phase == FarmPhase.Finished)
                browsingMeanwhile = false;

            // The title carries the state too, since the window is often behind
            // something else while a run is going.
            WindowName = session.Phase == FarmPhase.Paused
                ? $"AutoKill - paused - {session.Target.Name}###AutoKillMain"
                : $"AutoKill - {session.Target.Name}###AutoKillMain";

            if (!browsingMeanwhile)
            {
                DrawRun(mobs, session);
                return;
            }

            DrawMeanwhile(session);
        }
        else
        {
            WindowName = "AutoKill###AutoKillMain";
        }

        if (plannedTarget is not null)
        {
            DrawPlan(mobs, plannedTarget);
            return;
        }

        DrawBrowse(mobs);
    }

    /// <summary>The character logged in, or empty air while nobody is.</summary>
    private string Whose() => player.CharacterName is { Length: > 0 } name ? name : string.Empty;

    /// <summary>
    /// One line about the run this window has stepped away from, and the way
    /// back to it.
    /// </summary>
    private void DrawMeanwhile(FarmSession session)
    {
        Style.Place(session.Target.Name);

        ImGui.SameLine();
        if (Style.Quiet("back to the run"))
            browsingMeanwhile = false;

        Style.Trailing($"{session.Progress.Kills} killed   {session.Progress.Elapsed:hh\\:mm\\:ss}");
        ImGui.Separator();
    }

    private void DrawBrowse(MobIndex mobs)
    {
        using var tabs = ImRaii.TabBar("##autokill-tabs");
        if (!tabs)
            return;

        DrawMobTab(mobs);
        DrawDropTab(mobs);
        DrawHuntTab(mobs);
        DrawLogTab();
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
            Style.Nothing("No hunt bills in hand. Pick some up from a hunt board and they turn up here.");
            return;
        }

        using var child = ImRaii.Child("##hunts", new Vector2(-1, -1));
        if (!child)
            return;

        foreach (var bill in bills)
        {
            Style.Line(bill.Name);
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
    /// The hunting log: the rank each class is on, and what it still owes.
    /// </summary>
    /// <remarks>
    /// The log is the game's own kill list, and unlike a hunt bill it names who
    /// has to land the kill. So a run from here puts that class on rather than
    /// picking whatever clears the field fastest, and a class with no gearset
    /// to put on says so instead of offering a run that cannot count.
    ///
    /// Only the rank the client is on, because that is the only rank the game
    /// lets anyone work on. Only the current class is open when the tab is
    /// first drawn: twelve logs of ten entries is a wall, and the one being
    /// worn is the one being levelled.
    /// </remarks>
    private void DrawLogTab()
    {
        using var tab = ImRaii.TabItem("Log");
        if (!tab)
            return;

        if (logbook() is not { } book)
        {
            Style.Nothing("The hunting log turns up once the mob data has finished loading.");
            return;
        }

        var pages = book.Pages();
        if (pages.Count == 0)
        {
            Style.Nothing("No hunting log yet. It reads from the character, so it needs one logged in.");
            return;
        }

        // Walked once for the whole tab rather than once per log, since a
        // hundred gearset rows a frame is a hundred too many.
        var kitted = farming.Jobs.Gearsets().Select(gearset => gearset.Job.ClassJobId).ToHashSet();
        var worn = farming.Jobs.Current?.ClassJobId ?? 0;

        using var child = ImRaii.Child("##log", new Vector2(-1, -1));
        if (!child)
            return;

        // The ones that can actually be farmed first. Nine classes and a Grand
        // Company is a long list to read past when three of them are yours.
        foreach (var page in pages
                     .OrderBy(page => Access(page, kitted) == LogAccess.Open ? 0 : 1)
                     .ThenBy(page => page.Slot))
        {
            using var id = ImRaii.PushId(page.Slot);
            DrawLogPage(book, page, kitted, worn);
        }
    }

    /// <summary>Whether a log can be farmed, and what is in the way if not.</summary>
    /// <remarks>
    /// Two different walls, and which one it is matters: a class never picked
    /// up is a trip to its guild, and a class with no gearset is a minute in
    /// the character sheet. Both would otherwise read as the plugin simply not
    /// offering to do anything.
    /// </remarks>
    private enum LogAccess
    {
        Open,
        NotUnlocked,
        NoGearset,
    }

    private static LogAccess Access(LogPage page, IReadOnlySet<uint> kitted) =>
        !page.Unlocked ? LogAccess.NotUnlocked
        : page.ClassJobId != 0 && !kitted.Contains(page.ClassJobId) ? LogAccess.NoGearset
        : LogAccess.Open;

    private static string? Shut(LogAccess access) => access switch
    {
        LogAccess.NotUnlocked => "not unlocked",
        LogAccess.NoGearset => "no gearset",
        _ => null,
    };

    private void DrawLogPage(
        HuntingLog book, LogPage page, IReadOnlySet<uint> kitted, uint worn)
    {
        var access = Access(page, kitted);
        var left = page.Lines.Count - page.Done;

        // A class nobody has picked up has no level and no rank worth reading,
        // so its header is the name and the reason and nothing else.
        var title = access == LogAccess.NotUnlocked
            ? page.Name
            : page.ClassJobId == 0
                ? $"{page.Name}, rank {page.Rank} of {page.Ranks}"
                : $"{page.Name} Lv{page.Level}, rank {page.Rank} of {page.Ranks}";

        var flags = access == LogAccess.Open && page.ClassJobId == worn
            ? ImGuiTreeNodeFlags.DefaultOpen
            : ImGuiTreeNodeFlags.None;

        // A log that cannot be farmed is dimmed rather than hidden, since what
        // it wants is still worth reading before going and unlocking it.
        if (access != LogAccess.Open)
            ImGui.PushStyleColor(ImGuiCol.Text, Style.Muted);

        var shown = ImGui.CollapsingHeader($"{title}###log{page.Slot}", flags);

        if (access != LogAccess.Open)
            ImGui.PopStyleColor();

        Style.Trailing(
            Shut(access) ?? (left == 0 ? "rank done" : $"{page.Done}/{page.Lines.Count}"));

        if (!shown)
            return;

        using var indent = ImRaii.PushIndent();

        var wearable = access == LogAccess.Open;
        switch (access)
        {
            case LogAccess.NotUnlocked:
                Style.Muffled($"No {page.Name} yet. The log opens when the class does.");
                break;

            // The game offers no way to equip a bare class, so a log for one
            // that has never been kitted out cannot be farmed at all. Said
            // once, at the top, rather than against every entry underneath.
            case LogAccess.NoGearset:
                Style.Muffled($"No {page.Name} gearset, so nothing here can be run as one.");
                break;
        }

        foreach (var line in page.Lines)
        {
            using var id = ImRaii.PushId((int)line.Entry.RowId);
            DrawLogLine(book, page, line, wearable);
        }

        if (wearable && left > 0)
        {
            Style.Gap(2f);
            var route = book.Route(page, config.LogReach);
            if (route.Count == 0 && page.ClassJobId != 0)
            {
                Style.Muffled(
                    $"Nothing left within {config.LogReach} levels of {page.Level}. "
                    + "Level it a little, or reach further in settings.");
            }
            else if (route.Count == 0)
            {
                Style.Muffled("Nowhere recorded for what is left of this rank.");
            }
            else if (Style.Commit(
                         $"Farm rank {page.Rank}",
                         $"{route.Count} stop(s), each going after every mob of this rank standing in it."))
            {
                FarmLog(book, page, route);
            }
        }

        Style.Gap(2f);
    }

    private void DrawLogLine(
        HuntingLog book, LogPage page, LogLine line, bool wearable)
    {
        if (line.Entry.Done)
        {
            Style.Muffled(line.Names);
            ImGui.SameLine();
            ImGui.TextColored(Style.Good, "done");
            return;
        }

        var level = book.LevelOf(line, page);

        Style.Line(line.Names);
        if (level is { } written)
        {
            ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X * 0.75f);
            ImGui.TextColored(Style.Muted, $"Lv{written}");
        }

        Style.Trailing($"{line.Entry.Needed - line.Entry.Remaining}/{line.Entry.Needed}");

        using var indent = ImRaii.PushIndent();

        // One line each only when there is more than one, since a single mob
        // has already been named and counted above.
        if (line.Entry.Kills.Count > 1)
        {
            foreach (var kill in line.Entry.Kills)
            {
                Style.Muffled(kill.Name);
                Style.Trailing($"{kill.Killed}/{kill.Needed}");
            }
        }

        Style.Muffled(line.Zones);

        if (!line.Reachable)
        {
            // The Grand Company logs send you into dungeons, which is not
            // somewhere a run can go.
            Style.Muffled("nowhere recorded in that zone");
            return;
        }

        if (!wearable)
            return;

        // A Grand Company log names no class, so there is nothing pinned to be
        // out of reach of: whatever suits the field goes.
        var reach = page.ClassJobId == 0
                    || HuntingLogPlan.WithinReach(level, page.Level, config.LogReach);
        if (!reach)
        {
            Style.Muffled($"above {page.Level} + {config.LogReach}");
            ImGui.SameLine();
        }

        // A Grand Company log belongs to no class, so there is no "as" to say.
        var asClass = page.ClassJobId == 0 ? string.Empty : $", as {page.Name}";

        if (Style.Quiet(
                "farm this one",
                reach
                    ? $"Go after it now{asClass}."
                    : $"Go anyway{asClass}, even though it is over your reach."))
        {
            FarmLog(book, page, book.Route(page, config.LogReach, line.Entry.RowId));
        }
    }

    /// <summary>
    /// Send a hunting log route off as a queue of stops.
    /// </summary>
    /// <remarks>
    /// What each stop owes is asked again when that stop starts rather than
    /// now, because the stops before it have been killing the same rank and one
    /// of them may have finished it outright.
    /// </remarks>
    private void FarmLog(
        HuntingLog book,
        LogPage page,
        IReadOnlyList<(FarmTarget Target, IReadOnlyList<HuntingLogKill> Kills)> route)
    {
        if (route.Count == 0)
            return;

        farming.StartMany(route.Select(stop =>
        {
            var mobs = stop.Kills.Select(kill => kill.BNpcNameId).ToList();
            return new FarmLeg(stop.Target, () => book.Owing(page.Slot, mobs), page.ClassJobId);
        }));
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
            Style.Nothing("No runs yet. The runs you finish are filed here.");
            return;
        }

        ImGui.Spacing();
        if (Style.Row("clear history", "Forget every run filed here."))
            history.ForgetEverything();

        ImGui.Separator();

        using var child = ImRaii.Child("##history", new Vector2(-1, -1));
        if (!child)
            return;

        foreach (var run in history.Records.ToList())
        {
            using var id = ImRaii.PushId(run.When.Ticks.GetHashCode());

            var elapsed = TimeSpan.FromSeconds(run.ElapsedSeconds);

            Style.Line(run.Named);
            Style.Trailing($"{run.When:d MMM HH:mm}");

            using var indent = ImRaii.PushIndent();

            Style.Line(mobs.ZoneName(run.TerritoryId));

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
                if (Style.Quiet("repeat", "Plan this ground again, with the same goals."))
                    Repeat(mobs, run);
            }
            else
            {
                Style.Muffled("that ground is no longer in the data");
            }

            if (Style.TrailingRemove("Forget this run."))
                history.Forget(run);

            Style.Gap(2f);
        }
    }

    /// <summary>
    /// What the goals just set are likely to cost, going by every past run
    /// over this ground.
    /// </summary>
    /// <remarks>
    /// Answered before Start rather than after it, because "is this worth
    /// twenty minutes" is a question asked while deciding, and by then the
    /// window already knows: it has the kills, the items and the minutes of
    /// every run it has filed here. Runs are pooled rather than taking the
    /// last one, since one unlucky trip is not a pace.
    /// </remarks>
    private void DrawLikelyCost(MobIndex mobs, FarmTarget target)
    {
        if (!past.Anything(target))
            return;

        var said = false;

        if (killTarget > 0
            && Pace.TimeToGo(0, killTarget, TimeSpan.Zero, past.KillsPace(target)) is { } forKills)
        {
            Style.Muffled($"~{Pace.Roughly(forKills)} for {killTarget} kills, going by past runs here.");
            said = true;
        }

        foreach (var (itemId, wanted) in itemGoals)
        {
            if (Pace.TimeToGo(0, wanted, TimeSpan.Zero, past.PaceOf(target, itemId)) is not { } forItem)
                continue;

            Style.Muffled(
                $"~{Pace.Roughly(forItem)} for {wanted} {mobs.ItemName(itemId)}, going by past runs here.");
            said = true;
        }

        if (said)
            Style.Gap(2f);
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
            Style.Nothing("Nothing learnt yet. Farm something and this fills in.");
            return;
        }

        ImGui.Spacing();
        if (Style.Row("forget everything", "Throw away everything farming has taught the plugin."))
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
            Style.Line(mobs.Get(mobId)?.Name ?? $"mob {mobId}");

            ImGui.TableNextColumn();
            Style.Line(mobs.ZoneName(territoryId));

            ImGui.TableNextColumn();
            Style.Line(what.Kills.ToString());

            ImGui.TableNextColumn();
            if (what.Typical() is { } typical)
            {
                Style.Line($"{typical.Typical.TotalSeconds:F0}s");
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
            if (Style.Row("forget"))
                observations.Forget(mobId, territoryId);
        }
    }

    private void DrawRun(MobIndex mobs, FarmSession session)
    {
        var progress = session.Progress;
        var finished = session.Phase == FarmPhase.Finished;
        var paused = session.Phase == FarmPhase.Paused;

        Style.Line(session.Target.Name);
        Style.Level(session.Area.Level);
        Style.Muffled(Where(session.Area));
        Style.Trailing(Density(session.Area));

        Style.Gap(2f);

        // Paused is the one state the run wants somebody to notice, since it
        // does not end on its own.
        ImGui.TextColored(
            finished ? Style.Good : paused ? Style.Accent : Style.Muted,
            finished
                ? $"finished: {session.Status}"
                : $"{session.Phase.ToString().ToLowerInvariant()}: {session.Status}");

        if (!finished && farming.Queued > 0)
            Style.Muffled($"then {farming.Queued} more stop(s) for the list");

        Style.Gap();
        Style.Heading("progress");

        var kills = session.Conditions.Conditions.OfType<KillCountCondition>().FirstOrDefault();
        var time = session.Conditions.Conditions.OfType<ElapsedCondition>().FirstOrDefault();

        if (kills is null)
        {
            Style.Line("kills");
            Style.Trailing(progress.Kills.ToString());
        }
        else
        {
            Style.Progress(
                "kills", progress.Kills, kills.Target,
                Estimate.Reads(
                    progress.Kills, kills.Target, progress.Elapsed,
                    past.KillsPace(session.Target)));
        }

        if (time is null)
        {
            Style.Line("elapsed");
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

        // The hunting log counts each mob on its own: one field usually holds
        // several entries' worth of different mobs, and the total says nothing
        // about which of them is still owed.
        foreach (var mob in session.Conditions.Conditions.OfType<MobKillCondition>())
            // Clamped: a field holds more than one of them, so a target can be
            // overshot by whatever else was already swinging, and "8 / 3" reads
            // as a fault rather than as done.
            Style.Progress(
                mob.Name, Math.Min(progress.KillsOf(mob.BNpcNameId), mob.Target), mob.Target);

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
                    mobs.ItemName(itemId), have, target,
                    Estimate.Reads(have, target, progress.Elapsed, past.PaceOf(session.Target, itemId)));
            }
            else
            {
                Style.Line(mobs.ItemName(itemId));
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

            // The machinery movers are real buttons; the two ways of merely
            // looking elsewhere read as text until the mouse finds them.
            if (paused)
            {
                if (Style.Row("resume"))
                    farming.Resume();
            }
            else if (Style.Row("pause"))
            {
                farming.Pause();
            }

            ImGui.SameLine();
            if (Style.Row("stop"))
                farming.Stop();

            ImGui.SameLine();
            if (Style.Quiet("adjust targets", "Move the stop line without stopping the run."))
            {
                LoadTargets(session);
                adjusting = true;
            }

            ImGui.SameLine();
            if (Style.Quiet("browse", "Look something else up while this keeps going."))
                browsingMeanwhile = true;
            return;
        }

        adjusting = false;

        // Dying ends the run, but it should not cost the setup. What is left
        // is the same ask less what the run banked: kills made, items held and
        // time spent all stay made, held and spent.
        // Nothing to pick back up when the run got everything it asked for and
        // died on the way out: what is left of it would ask for nothing at all.
        if (session.Died && session.Outcome is { } outcome
                         && session.Conditions.Remaining(outcome).Asking)
        {
            // As the same class, since a hunting log counts the kill for one
            // class and picking the run back up as something else would farm
            // the same ground for nothing.
            if (Style.Row("pick it back up", "Start again, going for what the run still owed.")
                && farming.Start(session.Target, session.Conditions.Remaining(outcome), session.As))
            {
                resultDismissed = false;
                return;
            }

            ImGui.SameLine();
        }

        if (Style.Quiet("done"))
        {
            resultDismissed = true;
            StepBack();
        }

        ImGui.SameLine();
        if (!Style.Quiet("farm this again", "Back to the plan, with the same ground picked."))
            return;

        resultDismissed = true;
        Plan(session.Target);
    }

    /// <summary>
    /// Land somewhere the finished run makes useful.
    /// </summary>
    /// <remarks>
    /// A run picked off a crafting list ends back on that list, counts fresh
    /// and the next thing to farm in view. The material's own field list, which
    /// is where the window would otherwise return, is the one screen the list
    /// never wants next: either the material is done, or the way to go back for
    /// more is to pick it off the list again, which also picks up the amount.
    /// </remarks>
    private void StepBack()
    {
        if (craftingList is not null)
            selectedItem = 0;
    }

    /// <summary>
    /// Where back is, on the same line as the choice to go there at all. Only
    /// two answers, so a combo rather than radio buttons taking a line each.
    /// </summary>
    private void DrawReturnTo()
    {
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Style.Px(160f));

        var choice = config.ReturnDestination == ReturnDestination.Home ? 1 : 0;
        if (!ImGui.Combo("##return-to", ref choice, "to where I started\0to my home point\0"))
            return;

        config.ReturnDestination = choice == 1 ? ReturnDestination.Home : ReturnDestination.Start;
        saveConfig();
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
        if (Style.Commit("apply", "The run chases these targets from here on."))
        {
            session.Retarget(BuildConditions(session.Conditions));
            adjusting = false;
        }

        ImGui.SameLine();
        if (Style.Quiet("never mind"))
            adjusting = false;
    }

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
        Style.Muffled(Where(area));

        // Coordinates in a row are trusted blind; the map is how the game
        // answers "where is this exactly", so offer it before start is pressed.
        ImGui.SameLine();
        if (Style.Quiet("map", "Flag it on the map and open the map there."))
            farming.ShowOnMap(area);

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
        DrawLikelyCost(mobs, target);

        // On the plan rather than only in Settings, because whether coming home
        // is wanted depends on the run: it sticks as the default for the next
        // one, which is what a preference asked at the right moment does.
        var back = config.ReturnWhenDone;
        if (ImGui.Checkbox("teleport back when it ends", ref back))
        {
            config.ReturnWhenDone = back;
            saveConfig();
        }

        Style.Explain(
            "Once the run ends on its own. A run you stop yourself stays where it is.");

        if (config.ReturnWhenDone)
            DrawReturnTo();

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

        // Never two runs. Starting used to quietly replace whatever was going,
        // which was fine when the plan could not be reached mid-run and is a
        // trap now that it can.
        var busy = farming.Running;
        if (busy)
        {
            ImGui.TextColored(Style.Muted, "a run is already going; stop it to start this one");
            Style.Gap(2f);
        }

        // The one control on this screen that moves the character.
        using (ImRaii.Disabled(job.Blocked || busy))
        {
            if (Style.Commit("start") && farming.Start(target, BuildConditions()))
            {
                resultDismissed = false;
                ClearPlan();
            }
        }

        ImGui.SameLine();
        if (Style.Quiet("back"))
            ClearPlan();
    }

    /// <summary>
    /// The stop line: kills, minutes and items, and whether one or all of them
    /// ends the run. Shared between the plan and adjusting mid-run, so the two
    /// read as the same controls because they are.
    /// </summary>
    private void DrawStopWhen(MobIndex mobs, FarmTarget target)
    {
        Style.Heading("stop when");

        ImGui.SetNextItemWidth(Style.Px(120f));
        ImGui.InputInt("kills", ref killTarget);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Style.Px(120f));
        ImGui.InputInt("minutes", ref minuteTarget);

        if (target.Drops.Count > 0)
        {
            Style.Heading(target.Shared ? "collect, from any of them" : "collect");
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
                ImGui.SetNextItemWidth(Style.Px(100f));
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

        Style.Heading("needs");

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
            Style.Line(requirement.Name);
            Style.Trailing(requirement.Detail);
        }

        Style.Gap();
        ImGui.Separator();
        Style.Heading("getting about");

        var mountDistance = config.MountDistance;
        ImGui.SetNextItemWidth(Style.Px(220f));
        if (ImGui.SliderFloat("mount with this much left to cover", ref mountDistance, 5f, 150f, "%.0f yalms"))
        {
            config.MountDistance = mountDistance;
            saveConfig();
        }

        Style.MuffledWrapped(
            "Ground still to walk, with attack range already taken off, "
            + "so it means the same on a caster as on a melee job.");

        Style.Gap(2f);
        var patience = config.RespawnPatienceSeconds;
        ImGui.SetNextItemWidth(Style.Px(220f));
        if (ImGui.SliderFloat("look a little longer before moving on", ref patience, 0f, 30f, "%.0f seconds"))
        {
            config.RespawnPatienceSeconds = patience;
            saveConfig();
        }

        Style.MuffledWrapped(
            "Not a respawn wait: nothing comes back this quickly. It only stops "
            + "a moment with nothing in view from sending it somewhere else.");

        Style.Gap();
        ImGui.Separator();
        Style.Heading("going as the wrong job");

        Style.MuffledWrapped(
            "A crafter stands in the field doing nothing, and a battle job too "
            + "far down dies in it. Neither says why, so this is checked first.");
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
        Style.Heading("the hunting log");

        var reach = config.LogReach;
        ImGui.SetNextItemWidth(Style.Px(220f));
        if (ImGui.SliderInt("reach this far above the class", ref reach, 0, 10, "%d levels"))
        {
            config.LogReach = reach;
            saveConfig();
        }

        Style.MuffledWrapped(
            "The log is ordered by level and the class levels while it runs, so "
            + "this decides how much of a rank is offered at once. None of it "
            + "changes job: a log only counts for the class it belongs to.");

        Style.Gap();
        ImGui.Separator();
        Style.Heading("while it runs");

        var overlay = config.ShowOverlay;
        if (ImGui.Checkbox("small progress window while it runs", ref overlay))
        {
            config.ShowOverlay = overlay;
            saveConfig();
        }

        Style.MuffledWrapped(
            "Floats beside the game with the bars and the pause and stop "
            + "buttons, and goes away when the run ends.");

        Style.Gap(2f);
        var notifications = config.Notifications;
        if (ImGui.Checkbox("announce starts and finishes", ref notifications))
        {
            config.Notifications = notifications;
            saveConfig();
        }

        // The chat line and toast are silent, and a run ends precisely when
        // nobody is looking at the screen.
        var sound = config.FinishSound;
        ImGui.SetNextItemWidth(Style.Px(160f));
        if (ImGui.SliderInt(
                "sound when a run ends", ref sound, 0, 16, sound == 0 ? "none" : $"<se.{sound}>"))
        {
            config.FinishSound = sound;
            saveConfig();
        }

        if (config.FinishSound > 0)
        {
            ImGui.SameLine();
            if (Style.Quiet("play", "Ring it once, to hear which one this is."))
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
            ImGui.SetNextItemWidth(Style.Px(160f));
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
        Style.Heading("afterwards");

        var returnWhenDone = config.ReturnWhenDone;
        if (ImGui.Checkbox("teleport back when a run ends", ref returnWhenDone))
        {
            config.ReturnWhenDone = returnWhenDone;
            saveConfig();
        }

        if (config.ReturnWhenDone)
            DrawReturnTo();

        Style.MuffledWrapped(
            "Once a run ends on its own. One you stop yourself stays where "
            + "it is, and so does one that ends by dying.");

        Style.Gap(2f);
        var record = config.RecordRuns;
        if (ImGui.Checkbox("record runs to a trace file", ref record))
        {
            config.RecordRuns = record;
            saveConfig();
        }

        Style.MuffledWrapped(
            "One file per run under the plugin's config folder, "
            + "recording where it went and what was standing nearby.");
    }

    private void DrawItemIcon(MobIndex mobs, uint itemId) => DrawIcon(mobs.ItemIcon(itemId));

    private void DrawIcon(ushort icon) => Icons.Draw(textures, icon);

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

    /// <param name="keeping">
    /// The run's current targets, when this is an adjustment rather than a
    /// fresh plan. Anything this form has no control for rides along untouched:
    /// a hunting log run counts each mob on its own, and setting a kill count
    /// on top of it should not quietly throw that away.
    /// </param>
    private StopConditions BuildConditions(StopConditions? keeping = null)
    {
        var conditions = new List<IStopCondition>();

        if (keeping is not null)
            conditions.AddRange(keeping.Conditions.OfType<MobKillCondition>());

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

        if (Style.Quiet("back"))
        {
            selectedItem = 0;
            return;
        }

        ImGui.SameLine();
        DrawItemIcon(mobs, selectedItem);
        Style.Line(selectedItemName);
        if (selectedItemWanted > 1)
            Style.Trailing($"{selectedItemWanted} wanted");

        ImGui.Separator();
        Style.Gap(2f);

        var droppers = mobs.MobsDropping(selectedItem);
        if (droppers.Count == 0)
        {
            Style.Nothing("Nothing known drops this.");
            return;
        }

        DrawFields(mobs.FieldsDropping(selectedItem));

        // Said rather than left out. A mob with nowhere recorded is a gap in the
        // data, and silently dropping it reads as the mob not dropping the item.
        var nowhere = droppers.Where(mob => !mob.Farmable).ToList();
        if (nowhere.Count == 0)
            return;

        Style.Gap();
        Style.MuffledWrapped(
            $"Also dropped by {Phrases.Kinds(nowhere.Select(mob => mob.Name).ToList())}, "
            + "with nowhere recorded to find them.");
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
            if (Style.Quiet(distinct[i]))
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
        Style.Line("go as");
        ImGui.SameLine();

        ImGui.SetNextItemWidth(Style.Px(240f));
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
            Style.Nothing(rest > 0
                ? $"Nothing on this list is worth farming a mob for. Its {rest} material(s) are gathered, bought or crafted."
                : "Nothing on this list is worth farming a mob for.");
            return;
        }

        DrawFarmTheList(mobs, farmable);

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

    /// <summary>
    /// One button for the whole list: every outstanding material with a known
    /// field, farmed one stop after another.
    /// </summary>
    /// <remarks>
    /// Stops in the same zone are kept together, since the teleport between
    /// zones is the expensive part of the trip. Each stop's goal is worked out
    /// when it starts rather than here, so whatever earlier stops put in the
    /// bags is already counted. What cannot be taken along is said: a material
    /// with no recorded field would otherwise just quietly not turn up.
    /// </remarks>
    private void DrawFarmTheList(MobIndex mobs, IReadOnlyList<ListMaterial> farmable)
    {
        var wanted = farmable
            .Where(m => CraftingLists.StillNeeded(m.Required, Bags.CountOf(m.ItemId)) > 0)
            .ToList();

        if (wanted.Count == 0)
            return;

        var legs = wanted
            .Select(m => (Material: m, Field: mobs.FieldsDropping(m.ItemId).FirstOrDefault()))
            .Where(x => x.Field is not null)
            .Select(x => FarmController.Gathering(x.Field!, x.Material.ItemId, x.Material.Required))
            .GroupBy(leg => leg.Target.Area.TerritoryTypeId)
            .SelectMany(zone => zone)
            .ToList();

        if (legs.Count == 0)
            return;

        using (ImRaii.Disabled(farming.Running))
        {
            if (Style.Commit(
                    legs.Count == 1 ? "farm what is left" : $"farm the whole list, {legs.Count} stops",
                    "One run per material, in zone order, each going for what is still missing."))
            {
                farming.StartMany(legs);
            }
        }

        if (legs.Count < wanted.Count)
            Style.Muffled($"{wanted.Count - legs.Count} of them have nowhere recorded, so they stay behind.");

        ImGui.Separator();
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
