using AutoKill.Core;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace AutoKill.Farming;

/// <summary>
/// What the character currently is, what else it could be, and putting it in
/// something that can fight.
/// </summary>
/// <remarks>
/// Gearsets rather than the jobs themselves, because a job with no gear is not
/// somewhere worth being sent and the game offers no way to equip a bare class.
/// A gearset is also what somebody has already decided is their kit for that
/// job, which is a better answer than anything worked out here.
///
/// Levels are read per class out of the player's own record rather than from the
/// gearset, so a gearset built at level 30 and worn to 90 reads as 90.
/// </remarks>
public sealed class Jobs(
    IObjectTable objects,
    IDataManager data,
    ICondition condition,
    Configuration config,
    IPluginLog log)
{
    /// <summary>
    /// Which class or job the character is in, or nothing while there is no
    /// character to ask about.
    /// </summary>
    public JobStanding? Current =>
        objects.LocalPlayer is { } player && player.ClassJob.ValueNullable is { } job
            ? new JobStanding(job.RowId, Named(job), Doing(job), player.Level)
            : null;

    /// <summary>Every gearset that exists, as somewhere the run could go instead.</summary>
    public unsafe IReadOnlyList<JobSwitch> Gearsets()
    {
        var module = RaptureGearsetModule.Instance();
        if (module == null)
            return [];

        var sheet = data.GetExcelSheet<ClassJob>();
        var found = new List<JobSwitch>();
        var entries = module->Entries;

        for (var i = 0; i < entries.Length; i++)
        {
            ref var entry = ref entries[i];

            if ((entry.Flags & RaptureGearsetModule.GearsetFlag.Exists) == 0)
                continue;
            if (!sheet.TryGetRow(entry.ClassJob, out var job))
                continue;

            found.Add(new JobSwitch(
                entry.Id,
                entry.NameString,
                new JobStanding(job.RowId, Named(job), Doing(job), LevelOfClass(job))));
        }

        return found;
    }

    /// <summary>
    /// What starting a run against this would do as things stand.
    /// </summary>
    /// <remarks>
    /// Drawn every frame while a target is on screen, so the gearsets are only
    /// walked when there is actually something wrong and something to be done
    /// about it. In the ordinary case this costs one sheet lookup.
    /// </remarks>
    public JobPlan Plan(LevelRange? target)
    {
        if (Current is not { } job)
            return Nothing;

        var policy = config.JobPolicy;
        var gearsets = policy == JobPolicy.Switch && !JobFitness.Fit(job, target)
            ? Gearsets()
            : [];

        return JobFitness.Plan(job, target, gearsets, policy, config.PreferredJob);
    }

    /// <summary>
    /// What starting a run would do when the class is not ours to choose.
    /// </summary>
    /// <remarks>
    /// The hunting log names who has to land the kill, so the gearsets are
    /// walked every time rather than only when something is wrong: the class
    /// being asked for is usually not the one being worn, and that is the
    /// ordinary case here rather than the exception.
    /// </remarks>
    public JobPlan PlanAs(LevelRange? target, uint classJobId)
    {
        if (Current is not { } job)
            return Nothing;

        var named = data.GetExcelSheet<ClassJob>().GetRowOrDefault(classJobId);
        return JobFitness.PlanAs(
            job,
            target,
            Gearsets(),
            classJobId,
            named is { } row ? Named(row) : "that class",
            config.JobPolicy);
    }

    /// <summary>
    /// The battle jobs you have a gearset for, each once, for picking a
    /// favourite out of.
    /// </summary>
    /// <remarks>
    /// Jobs rather than gearsets, because two gearsets for one job are the same
    /// answer to this question, and a preference for one of them would be a
    /// preference about gear rather than about what to go as.
    /// </remarks>
    public IReadOnlyList<JobStanding> Choices() =>
        Gearsets()
            .Select(gearset => gearset.Job)
            .Where(job => job.Battle)
            .GroupBy(job => job.ClassJobId)
            .Select(job => job.First())
            .OrderBy(job => job.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Put the gearset on. False means it was refused, and the reason is nearly
    /// always that the character is in combat.
    /// </summary>
    /// <remarks>
    /// The change is not instant: the game takes a moment to swap the gear and
    /// the job over. Nothing waits for it, because what follows is a teleport
    /// and a flight, and both take far longer than the swap does.
    /// </remarks>
    public unsafe bool Equip(JobSwitch to)
    {
        if (condition[ConditionFlag.InCombat])
        {
            log.Information($"Not changing to gearset {to.GearsetId} while in combat.");
            return false;
        }

        var module = RaptureGearsetModule.Instance();
        if (module == null || !module->IsValidGearset(to.GearsetId))
            return false;

        var result = module->EquipGearset(to.GearsetId);
        if (result < 0)
        {
            log.Warning($"The game refused gearset {to.GearsetId} ({to.GearsetName}), code {result}.");
            return false;
        }

        log.Information($"Changed to {to.Job.Name} from gearset {to.GearsetId} ({to.GearsetName}).");
        return true;
    }

    private static readonly JobPlan Nothing = new(JobProblem.None, null, false, null);

    /// <summary>
    /// What this class does in a fight, if anything.
    /// </summary>
    /// <remarks>
    /// The sheet's own numbering, which JobRole is written to match: 1 tank,
    /// 2 melee, 3 ranged, 4 healer, and zero for everything that has no place in
    /// a fight at all. Anything the sheet grows later reads as none, which keeps
    /// an unknown job out of a field rather than sending it to one.
    /// </remarks>
    private static JobRole Doing(ClassJob job) =>
        Enum.IsDefined(typeof(JobRole), (int)job.Role) ? (JobRole)job.Role : JobRole.None;

    /// <summary>
    /// How far this class has got, whether or not it is being worn.
    /// </summary>
    /// <remarks>
    /// Levels are held per experience slot rather than per class row, which is
    /// how a class and the job it becomes share one level. A row with no slot is
    /// nothing anyone levels.
    /// </remarks>
    public static unsafe int LevelOfClass(ClassJob job)
    {
        var state = PlayerState.Instance();
        if (state == null || job.ExpArrayIndex < 0)
            return 0;

        var levels = state->ClassJobLevels;
        return job.ExpArrayIndex < levels.Length ? levels[job.ExpArrayIndex] : 0;
    }

    // The sheets keep these in lower case, the same as mob names, and for the
    // same reason they are put back up here: this ends up in a sentence.
    private static string Named(ClassJob job) => Phrases.Capitalise(job.Name.ExtractText());
}
