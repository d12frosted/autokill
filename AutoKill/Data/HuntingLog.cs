using AutoKill.Core;
using AutoKill.Farming;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace AutoKill.Data;

/// <summary>One line of the log, with somewhere to go for each mob on it.</summary>
/// <param name="Where">
/// Every territory each mob could be killed in, out of the zones the entry
/// itself names. Empty for a mob nobody has recorded standing there, which is
/// what the Grand Company logs' dungeon entries look like from here.
/// </param>
public sealed record LogLine(
    HuntingLogEntry Entry,
    IReadOnlyDictionary<uint, IReadOnlyList<uint>> Where,
    string Zones)
{
    /// <summary>Whether every mob on it has somewhere to be farmed.</summary>
    public bool Reachable => Entry.Kills.All(kill => Where[kill.BNpcNameId].Count > 0);

    public string Names => Phrases.Kinds(Entry.Kills.Select(kill => kill.Name).ToList());
}

/// <summary>
/// One hunting log: a class's or a Grand Company's, and the rank it is on.
/// </summary>
/// <param name="Slot">Where the client keeps it, which is also the order they come in.</param>
/// <param name="ClassJobId">
/// The class that has to land the kill, or zero for a Grand Company log, which
/// names no class and counts whoever does it.
/// </param>
/// <param name="Level">
/// How far the class itself has got, whoever is wearing it, and zero for a
/// Grand Company log, which belongs to no class.
/// </param>
/// <param name="Unlocked">
/// Whether this log is open at all. A class nobody has ever picked up has no
/// log to fill in, and the game does not show one either.
/// </param>
public sealed record LogPage(
    int Slot,
    string Name,
    uint ClassJobId,
    int Level,
    int Rank,
    int Ranks,
    bool Unlocked,
    IReadOnlyList<LogLine> Lines)
{
    public IEnumerable<LogLine> Left => Lines.Where(line => !line.Entry.Done);

    /// <summary>How many of the rank's entries are finished.</summary>
    public int Done => Lines.Count(line => line.Entry.Done);

    public int Remaining => Lines.Sum(line => line.Entry.Remaining);
}

/// <summary>
/// The hunting log, as the game keeps it: what each class has left to kill and
/// how far it has got.
/// </summary>
/// <remarks>
/// All of it is in the client. `MonsterNote` holds the entries and
/// `MonsterNoteTarget` the mobs they name, keyed by `BNpcName` like everything
/// else here, and `MonsterNoteManager` holds the counts.
///
/// Nothing in the sheets says which class an entry belongs to. The row id does:
/// the game's own agent builds it as `ClassId * BaseId + Rank * 10 + index + 1`,
/// which is the ClassJob row times ten thousand for a class and the Grand
/// Company row times a million for the other three, ten entries to a rank,
/// counted from zero.
///
/// The client carries one rank per log and no more, which is not a gap: a rank
/// opens when the one before it is finished, so the rank it is carrying is the
/// only rank there is anything to do about.
/// </remarks>
public sealed class HuntingLog
{
    private const uint ClassLogStride = 10_000;
    private const uint GrandCompanyLogStride = 1_000_000;
    private const int EntriesPerRank = 10;

    /// <summary>How many ranks a log has, class logs first.</summary>
    private const int ClassRanks = 5;
    private const int GrandCompanyRanks = 3;

    private readonly IDataManager data;
    private readonly MobIndex index;
    private readonly IPluginLog log;

    /// <summary>The twelve logs in the order the client keeps them.</summary>
    private readonly IReadOnlyList<LogSource> sources;

    /// <summary>Everything the sheets say, worked out once.</summary>
    private readonly Dictionary<uint, SheetEntry> entries;

    private bool warnedAboutSlots;

    public HuntingLog(IDataManager data, MobIndex index, IPluginLog log)
    {
        this.data = data;
        this.index = index;
        this.log = log;

        var places = Territories();
        entries = ReadSheets(data, places, log);
        sources = ReadSources(data, entries.Values);
    }

    /// <summary>Every log, with the rank it is on and what that rank has left.</summary>
    public unsafe IReadOnlyList<LogPage> Pages()
    {
        var manager = MonsterNoteManager.Instance();
        if (manager == null)
            return [];

        // The game shows the log of the Grand Company you belong to and no
        // other, so neither does this. None of them before you have joined one.
        var state = PlayerState.Instance();
        var company = state == null ? 0u : state->GrandCompany;

        var ranks = manager->RankData;
        var pages = new List<LogPage>();

        for (var slot = 0; slot < sources.Count && slot < ranks.Length; slot++)
        {
            var source = sources[slot];
            if (source.GrandCompanyId != 0 && source.GrandCompanyId != company)
                continue;

            ref var progress = ref ranks[slot];

            // The slot order is taken from the sheet rather than from the
            // client, so this is where a wrong guess about it would show up.
            if (progress.Index != slot && !warnedAboutSlots)
            {
                warnedAboutSlots = true;
                log.Warning(
                    $"Hunting log slot {slot} says it is {progress.Index}. "
                    + "The logs may be lined up wrong.");
            }

            var rank = progress.Rank;
            if (rank < 0 || rank >= source.Ranks)
            {
                log.Warning($"Hunting log {source.Name} is on rank {rank}, which it does not have.");
                continue;
            }

            var lines = new List<LogLine>();
            for (var i = 0; i < EntriesPerRank; i++)
            {
                var rowId = source.Base + (uint)(rank * EntriesPerRank + i + 1);
                if (!entries.TryGetValue(rowId, out var sheet))
                    continue;

                lines.Add(Line(sheet, progress.RankData[i]));
            }

            var level = source.ClassJobId == 0 ? 0 : LevelOf(source.ClassJobId);

            pages.Add(new LogPage(
                slot,
                source.Name,
                source.ClassJobId,
                level,
                rank + 1,
                source.Ranks,
                // A class at level zero has never been picked up, which is the
                // only way a class log is closed.
                source.ClassJobId == 0 || level > 0,
                lines));
        }

        return pages;
    }

    /// <summary>
    /// Where to go to finish what is left of a rank, one field at a time.
    /// </summary>
    /// <remarks>
    /// Entries are grouped into the fewest zones that hold all their mobs, and
    /// then each zone is broken into the fields inside it, because a zone with
    /// two mobs at opposite ends of it is two places to stand rather than one.
    /// Every mob wanted in a field is farmed at once, which is what a run after
    /// a set of mobs was for.
    /// </remarks>
    /// <param name="only">
    /// One entry's row, when that is all that was asked for. Its own level is
    /// then nobody's business but the presser's, since naming an entry is
    /// asking for it.
    /// </param>
    public IReadOnlyList<(FarmTarget Target, IReadOnlyList<HuntingLogKill> Kills)> Route(
        LogPage page, int allowance, uint only = 0)
    {
        var wanted = new Dictionary<uint, HuntingLogKill>();
        var placements = new List<HuntingLogPlacement>();

        foreach (var line in page.Left)
        {
            if (only != 0 && line.Entry.RowId != only)
                continue;

            // Only a class log is capped. A Grand Company log pins nothing, so
            // whatever suits the field is put on and the ordinary job check is
            // already the thing standing between a run and ground above it.
            if (only == 0
                && page.ClassJobId != 0
                && !HuntingLogPlan.WithinReach(LevelOf(line, page), page.Level, allowance))
                continue;

            foreach (var kill in line.Entry.Kills.Where(kill => !kill.Done))
            {
                var where = line.Where[kill.BNpcNameId];
                if (where.Count == 0)
                    continue;

                // The same mob on two entries is one thing to kill: the kill
                // counts for both, so the deeper of the two asks is the ask.
                if (wanted.TryGetValue(kill.BNpcNameId, out var already))
                {
                    if (already.Remaining >= kill.Remaining)
                        continue;
                    placements.RemoveAll(p => p.BNpcNameId == kill.BNpcNameId);
                }

                wanted[kill.BNpcNameId] = kill;
                placements.Add(new HuntingLogPlacement(kill.BNpcNameId, where));
            }
        }

        var route = new List<(FarmTarget, IReadOnlyList<HuntingLogKill>)>();

        foreach (var stop in HuntingLogPlan.Stops(placements))
        {
            var mobs = stop.BNpcNameIds
                .Select(index.Get)
                .OfType<MobEntry>()
                .ToList();

            foreach (var field in index.Fields(mobs))
            {
                if (field.Area.TerritoryTypeId != stop.TerritoryTypeId)
                    continue;

                var here = field.BNpcNameIds
                    .Where(stop.BNpcNameIds.Contains)
                    .Select(id => wanted[id])
                    .ToList();

                if (here.Count > 0)
                    route.Add((field, here));
            }
        }

        return route;
    }

    /// <summary>
    /// What these mobs still owe, asked again now rather than when the route
    /// was drawn up.
    /// </summary>
    /// <remarks>
    /// The stops before this one have been killing things, and one of them may
    /// have finished the rank outright, in which case the client is already on
    /// the next one and none of these mobs is wanted any more. Nothing to owe
    /// means nothing to do here, and the leg is skipped.
    /// </remarks>
    public StopConditions? Owing(int slot, IReadOnlyList<uint> mobs)
    {
        if (Pages().FirstOrDefault(page => page.Slot == slot) is not { } page)
            return null;

        var owed = page.Lines
            .SelectMany(line => line.Entry.Kills)
            .Where(kill => !kill.Done && mobs.Contains(kill.BNpcNameId))
            .GroupBy(kill => kill.BNpcNameId)
            .Select(mob => mob.MaxBy(kill => kill.Remaining)!)
            .ToList();

        return owed.Count == 0 ? null : Goal(owed);
    }

    /// <summary>What would finish this stop, and nothing else.</summary>
    /// <remarks>
    /// One target per mob, all of them, because a field usually holds several
    /// entries' worth of different mobs and a plain kill count would call the
    /// run done with half of them still owed. Dying and full bags end it as
    /// they end everything.
    /// </remarks>
    public static StopConditions Goal(IReadOnlyList<HuntingLogKill> kills) =>
        new(
        [
            .. kills.Select(kill =>
                (IStopCondition)new MobKillCondition(kill.BNpcNameId, kill.Name, kill.Remaining)),
            new DeathCondition(),
            new InventoryFullCondition(),
        ],
            StopMode.All);

    /// <summary>
    /// The level an entry was written for, or the ground's when the log does
    /// not say.
    /// </summary>
    public int? LevelOf(LogLine line, LogPage page)
    {
        if (line.Entry.Level is { } level)
            return level;

        // A Grand Company entry has no place in a level order to read, so the
        // only thing left to go on is what the ground was recorded at, and only
        // the ground the entry actually sends you to.
        int? highest = null;
        foreach (var kill in line.Entry.Kills)
        {
            var here = line.Where[kill.BNpcNameId];
            foreach (var area in index.Get(kill.BNpcNameId)?.Areas ?? [])
            {
                if (!here.Contains(area.TerritoryTypeId) || area.Level is not { } range)
                    continue;

                if (highest is null || range.Highest > highest)
                    highest = range.Highest;
            }
        }

        return highest;
    }

    private LogLine Line(SheetEntry sheet, RankData counts)
    {
        var kills = new List<HuntingLogKill>();
        var where = new Dictionary<uint, IReadOnlyList<uint>>();

        foreach (var target in sheet.Targets)
        {
            kills.Add(new HuntingLogKill(
                target.BNpcNameId, target.Name, target.Needed, counts[target.Slot]));

            var areas = index.Get(target.BNpcNameId)?.Areas ?? [];
            where[target.BNpcNameId] = areas
                .Select(area => area.TerritoryTypeId)
                .Where(target.Territories.Contains)
                .Distinct()
                .ToList();
        }

        return new LogLine(
            new HuntingLogEntry(sheet.RowId, sheet.Index, sheet.Rank, sheet.Level, kills),
            where,
            Phrases.List(sheet.Targets.SelectMany(t => t.Zones).Distinct().ToList()));
    }

    /// <summary>How far a class has got, whether or not it is being worn.</summary>
    private int LevelOf(uint classJobId) =>
        data.GetExcelSheet<ClassJob>().TryGetRow(classJobId, out var job)
            ? Jobs.LevelOfClass(job)
            : 0;

    /// <summary>Which territories answer to each zone name the log uses.</summary>
    private Dictionary<uint, List<uint>> Territories()
    {
        var byPlace = new Dictionary<uint, List<uint>>();
        foreach (var territory in data.GetExcelSheet<TerritoryType>())
        {
            var place = territory.PlaceName.RowId;
            if (place == 0 || territory.RowId == 0)
                continue;

            if (!byPlace.TryGetValue(place, out var list))
                byPlace[place] = list = [];
            list.Add(territory.RowId);
        }

        return byPlace;
    }

    private static Dictionary<uint, SheetEntry> ReadSheets(
        IDataManager data, Dictionary<uint, List<uint>> places, IPluginLog log)
    {
        var notes = data.GetExcelSheet<MonsterNote>();
        var names = data.GetExcelSheet<BNpcName>();
        var found = new Dictionary<uint, SheetEntry>();

        foreach (var note in notes)
        {
            var targets = new List<SheetTarget>();

            for (var slot = 0; slot < note.MonsterNoteTarget.Count && slot < note.Count.Count; slot++)
            {
                var needed = note.Count[slot];
                if (needed == 0 || note.MonsterNoteTarget[slot].ValueNullable is not { } target)
                    continue;

                var nameId = target.BNpcName.RowId;
                if (nameId == 0)
                    continue;

                var zones = new List<string>();
                var territories = new List<uint>();
                foreach (var zone in target.PlaceNameZone)
                {
                    if (zone.ValueNullable is not { } place || place.RowId == 0)
                        continue;

                    zones.Add(place.Name.ExtractText());
                    if (places.TryGetValue(place.RowId, out var inZone))
                        territories.AddRange(inZone);
                }

                targets.Add(new SheetTarget(
                    // The slot the sheet put it in, kept rather than counted,
                    // since it is also where the client keeps its kill count.
                    slot,
                    nameId,
                    // The sheets keep mob names in lower case, and a list of
                    // them reads as a mistake rather than as a list.
                    names.GetRowOrDefault(nameId) is { } name
                        ? Phrases.Capitalise(name.Singular.ExtractText())
                        : $"mob {nameId}",
                    needed,
                    zones,
                    territories));
            }

            // Every log is fifty rows wide in the sheet and a Grand Company one
            // fills thirty of them. The rest ask for nothing.
            if (targets.Count == 0)
                continue;

            var index = (int)(note.RowId >= GrandCompanyLogStride
                ? note.RowId % GrandCompanyLogStride
                : note.RowId % ClassLogStride);

            if (index is < 1 or > 50)
            {
                log.Warning($"Hunting log row {note.RowId} sits at {index}, which is not a place in a log.");
                continue;
            }

            found[note.RowId] = new SheetEntry(
                note.RowId,
                index,
                (index - 1) / EntriesPerRank + 1,
                // Where a class entry sits is the level it was written for.
                // A Grand Company log has no such order to read.
                note.RowId >= GrandCompanyLogStride ? null : index,
                targets);
        }

        return found;
    }

    /// <summary>
    /// The logs themselves, in the order the client keeps them, which is the
    /// order the row ids come in: the nine classes, then the three Grand
    /// Companies.
    /// </summary>
    private static IReadOnlyList<LogSource> ReadSources(
        IDataManager data, IEnumerable<SheetEntry> entries)
    {
        var classes = data.GetExcelSheet<ClassJob>();
        var companies = data.GetExcelSheet<GrandCompany>();

        return entries
            .Select(entry => entry.RowId >= GrandCompanyLogStride
                ? entry.RowId / GrandCompanyLogStride * GrandCompanyLogStride
                : entry.RowId / ClassLogStride * ClassLogStride)
            .Distinct()
            .Order()
            .Select(logBase => logBase >= GrandCompanyLogStride
                ? new LogSource(
                    logBase,
                    0,
                    logBase / GrandCompanyLogStride,
                    companies.GetRowOrDefault(logBase / GrandCompanyLogStride)
                        ?.Name.ExtractText() ?? "Grand Company",
                    GrandCompanyRanks)
                : new LogSource(
                    logBase,
                    logBase / ClassLogStride,
                    0,
                    Phrases.Capitalise(
                        classes.GetRowOrDefault(logBase / ClassLogStride)?.Name.ExtractText() ?? ""),
                    ClassRanks))
            .ToList();
    }

    private sealed record LogSource(
        uint Base, uint ClassJobId, uint GrandCompanyId, string Name, int Ranks);

    private sealed record SheetTarget(
        int Slot,
        uint BNpcNameId,
        string Name,
        int Needed,
        IReadOnlyList<string> Zones,
        IReadOnlyList<uint> Territories);

    private sealed record SheetEntry(
        uint RowId,
        int Index,
        int Rank,
        int? Level,
        IReadOnlyList<SheetTarget> Targets);
}
