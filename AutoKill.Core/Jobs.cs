namespace AutoKill.Core;

/// <summary>
/// What a class does in a fight, if anything.
/// </summary>
/// <remarks>
/// The numbers are the game's own, straight out of the ClassJob sheet, so the
/// plugin side casts rather than translating. Zero covers everything with no
/// place in a fight: the crafters, the gatherers, and the unclassed adventurer
/// you are before picking anything.
/// </remarks>
public enum JobRole
{
    None = 0,
    Tank = 1,
    Melee = 2,
    Ranged = 3,
    Healer = 4,
}

/// <summary>Which class or job the character is standing in, and how far it has got.</summary>
public sealed record JobStanding(uint ClassJobId, string Name, JobRole Role, int Level)
{
    /// <summary>
    /// Whether this one fights at all. Crafters and gatherers are classes like
    /// any other and level like any other, and none of that helps here.
    /// </summary>
    public bool Battle => Role != JobRole.None;
}

/// <summary>A gearset, as somewhere a run could put the character instead.</summary>
public sealed record JobSwitch(int GearsetId, string GearsetName, JobStanding Job);

/// <summary>Why the character cannot go and fight this as it stands.</summary>
public enum JobProblem
{
    None,

    /// <summary>Nothing this class does is fighting.</summary>
    NotABattleJob,

    /// <summary>It fights, but not this high up.</summary>
    Underlevelled,

    /// <summary>
    /// Something else named the class, and there is no gearset to put it on
    /// with. The game offers no way to equip a bare one.
    /// </summary>
    NoGearset,
}

/// <summary>What to do about a character that cannot fight what was picked.</summary>
public enum JobPolicy
{
    /// <summary>Nothing. Start anyway and let it play out.</summary>
    Ignore,

    /// <summary>Refuse to start, and say why.</summary>
    Refuse,

    /// <summary>Change into a gearset that can, and refuse only when none can.</summary>
    Switch,
}

/// <summary>
/// What starting a run right now would actually do.
/// </summary>
/// <param name="Change">A gearset to put on first, or nothing.</param>
/// <param name="Blocked">Whether the run should refuse to start at all.</param>
/// <param name="Says">
/// The whole of it in one line, for putting in front of somebody before they
/// press the button, or nothing when there is nothing worth saying.
/// </param>
public sealed record JobPlan(JobProblem Problem, JobSwitch? Change, bool Blocked, string? Says);

/// <summary>
/// Whether the character is in any state to go and kill the thing that was
/// asked for, and what it would take to be.
/// </summary>
/// <remarks>
/// Both ways of getting this wrong end the same way and neither says so. A
/// crafter walks to the field and stands in it, because there is no rotation to
/// run and nothing to run it with. A battle job twenty levels short walks to the
/// field and dies in it, repeatedly, since the run's answer to dying is to stop
/// but its answer to a long fight is to keep going. Both are worth catching
/// before the teleport rather than after.
///
/// Only what is recorded can block anything. Three percent of the spawn points
/// carry no level and 302 mobs carry none anywhere, so an unknown level has to
/// mean "no opinion" rather than "level zero", or a third of the game becomes
/// unfarmable on a technicality.
/// </remarks>
public static class JobFitness
{
    /// <summary>What is wrong with taking this job there, if anything.</summary>
    public static JobProblem Problem(JobStanding job, LevelRange? target)
    {
        if (!job.Battle)
            return JobProblem.NotABattleJob;

        if (target is null)
            return JobProblem.None;

        // The top of the range rather than the bottom. A field is patrolled
        // whole, and being able to kill the easiest thing standing in it is not
        // the question being asked.
        return job.Level >= target.Highest ? JobProblem.None : JobProblem.Underlevelled;
    }

    public static bool Fit(JobStanding job, LevelRange? target) =>
        Problem(job, target) == JobProblem.None;

    /// <summary>
    /// What is wrong, said the way it would be said out loud, or nothing when
    /// nothing is.
    /// </summary>
    public static string? Trouble(JobStanding job, LevelRange? target) => Problem(job, target) switch
    {
        JobProblem.NotABattleJob => $"{job.Name} cannot fight.",
        JobProblem.Underlevelled => $"{job.Name} is level {job.Level}, and this is {target}.",
        _ => null,
    };

    /// <summary>
    /// The gearset best suited to going there, or nothing when none of them is.
    /// </summary>
    /// <param name="preferred">
    /// A ClassJob row to reach for first, or zero for whatever suits it best.
    /// </param>
    /// <remarks>
    /// Everything here is decided among the gearsets that can actually manage
    /// the field. Nothing about a preference or a role sends something that will
    /// die there.
    ///
    /// A named job wins outright, because somebody who named one named it on
    /// purpose. After that it is what kills things fastest: farming is damage,
    /// and a tank clears a field slowly while a healer barely clears it at all.
    /// Melee and ranged are the same kind of answer, so the higher of the two
    /// goes.
    ///
    /// Only then the level, highest first, since everything dies faster on the
    /// higher one and the run is otherwise identical. Ties go to the earlier
    /// gearset, because the choice has to come out the same every time: a run
    /// refused once and started the next time, with nothing changed in between,
    /// is worse than either answer.
    /// </remarks>
    public static JobSwitch? Best(
        IEnumerable<JobSwitch> gearsets, LevelRange? target, uint preferred = 0) =>
        gearsets
            .Where(gearset => Fit(gearset.Job, target))
            .OrderByDescending(gearset => preferred != 0 && gearset.Job.ClassJobId == preferred)
            .ThenBy(gearset => Killing(gearset.Job.Role))
            .ThenByDescending(gearset => gearset.Job.Level)
            .ThenBy(gearset => gearset.GearsetId)
            .FirstOrDefault();

    /// <summary>How much use a role is when the whole job is emptying a field.</summary>
    private static int Killing(JobRole role) => role switch
    {
        JobRole.Melee or JobRole.Ranged => 0,
        JobRole.Tank => 1,
        _ => 2,
    };

    /// <summary>
    /// What starting a run right now would do, under the standing instruction
    /// about jobs that cannot manage it.
    /// </summary>
    /// <remarks>
    /// Refusing never looks at the gearsets. Somebody who asked to be stopped
    /// asked to be stopped, and offering a change they did not want reads as the
    /// setting being ignored.
    ///
    /// Ignoring still says something. Mobs a few levels up are perfectly
    /// killable and anyone who wants to try is entitled to, but a death nobody
    /// was warned about looks like a fault in the run rather than the fight it
    /// actually was.
    /// </remarks>
    public static JobPlan Plan(
        JobStanding job,
        LevelRange? target,
        IReadOnlyList<JobSwitch> gearsets,
        JobPolicy policy,
        uint preferred = 0)
    {
        var problem = Problem(job, target);
        if (problem == JobProblem.None)
            return new JobPlan(problem, null, false, null);

        var trouble = Trouble(job, target);

        return policy switch
        {
            JobPolicy.Ignore => new JobPlan(problem, null, false, $"{trouble} Going anyway."),
            JobPolicy.Refuse => new JobPlan(problem, null, true, trouble),
            _ => Best(gearsets, target, preferred) is { } change
                ? new JobPlan(problem, change, false, $"{trouble} Switching to {change.Job.Name} first.")
                : new JobPlan(problem, null, true, $"{trouble} Nothing you can switch to gets there."),
        };
    }

    /// <summary>
    /// What starting a run would do when the class is not this plugin's choice
    /// to make.
    /// </summary>
    /// <remarks>
    /// The hunting log only counts a kill for the class whose log it is, so
    /// everything Plan does is turned around. There is no picking the gearset
    /// that clears the field fastest: there is one class it can be, and either
    /// there is a gearset for it or the log cannot be farmed at all.
    ///
    /// Being under-levelled stops being a reason to change into something else,
    /// since changing is the one thing that must not happen, so it comes down to
    /// going anyway or refusing. Refusing still refuses, because somebody who
    /// asked to be stopped asked to be stopped.
    ///
    /// Among the gearsets for the class it is the earliest, since they are all
    /// the same class at the same level and the answer has to come out the same
    /// every time.
    /// </remarks>
    public static JobPlan PlanAs(
        JobStanding job,
        LevelRange? target,
        IReadOnlyList<JobSwitch> gearsets,
        uint classJobId,
        string className,
        JobPolicy policy)
    {
        if (job.ClassJobId != classJobId)
        {
            var wear = gearsets
                .Where(gearset => gearset.Job.ClassJobId == classJobId)
                .OrderBy(gearset => gearset.GearsetId)
                .FirstOrDefault();

            if (wear is null)
                return new JobPlan(
                    JobProblem.NoGearset,
                    null,
                    true,
                    $"You have no {className} gearset to put on.");

            var trouble = Trouble(wear.Job, target);
            if (trouble is null)
                return new JobPlan(
                    JobProblem.None,
                    wear,
                    false,
                    $"The log is {className}'s. Switching to it first.");

            return policy == JobPolicy.Refuse
                ? new JobPlan(Problem(wear.Job, target), null, true, trouble)
                : new JobPlan(
                    Problem(wear.Job, target),
                    wear,
                    false,
                    $"{trouble} Switching to {className} anyway.");
        }

        var problem = Problem(job, target);
        if (problem == JobProblem.None)
            return new JobPlan(problem, null, false, null);

        var wrong = Trouble(job, target);
        return policy == JobPolicy.Refuse
            ? new JobPlan(problem, null, true, wrong)
            : new JobPlan(problem, null, false, $"{wrong} Going anyway.");
    }
}
