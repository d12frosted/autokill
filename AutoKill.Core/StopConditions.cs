namespace AutoKill.Core;

/// <summary>Everything a stop condition is allowed to look at.</summary>
/// <param name="KillsByMob">
/// Kills broken out by what was killed. A run after a set of mobs is still one
/// count of kills for anything that only asks how many, but the hunting log
/// asks for three of this and three of that, and one is not the other.
/// </param>
public readonly record struct FarmProgress(
    int Kills,
    TimeSpan Elapsed,
    int Level,
    bool InventoryFull,
    bool Died,
    IReadOnlyDictionary<uint, int> ItemsGained,
    IReadOnlyDictionary<uint, int> KillsByMob)
{
    public int CountOf(uint itemId) => ItemsGained.GetValueOrDefault(itemId);

    public int KillsOf(uint bNpcNameId) => KillsByMob.GetValueOrDefault(bNpcNameId);
}

public interface IStopCondition
{
    bool IsMet(FarmProgress progress);

    string Describe(FarmProgress progress);

    /// <summary>
    /// Safety conditions end a run on their own terms. Dying or filling the bags
    /// does not become acceptable just because the kill target is unmet, so these
    /// are never held back waiting for the rest of an all-of set.
    /// </summary>
    bool IsSafety => false;
}

public sealed class KillCountCondition(int target) : IStopCondition
{
    public int Target { get; } = target;

    public bool IsMet(FarmProgress progress) => progress.Kills >= Target;

    public string Describe(FarmProgress progress) => $"kills {progress.Kills}/{Target}";
}

/// <summary>So many of one kind of mob, whatever else is standing in the field.</summary>
/// <remarks>
/// The hunting log's unit. An entry wants three of a named mob, and a field
/// often holds two or three entries' worth of different mobs at once, so a
/// plain kill count would call the run done while half the entries were still
/// owed.
/// </remarks>
public sealed class MobKillCondition(uint bNpcNameId, string name, int target) : IStopCondition
{
    public uint BNpcNameId { get; } = bNpcNameId;

    public string Name { get; } = name;

    public int Target { get; } = target;

    public bool IsMet(FarmProgress progress) => progress.KillsOf(BNpcNameId) >= Target;

    public string Describe(FarmProgress progress) =>
        $"{Name} {progress.KillsOf(BNpcNameId)}/{Target}";
}

public sealed class ItemCountCondition(uint itemId, int target) : IStopCondition
{
    public uint ItemId { get; } = itemId;

    public int Target { get; } = target;

    public bool IsMet(FarmProgress progress) => progress.CountOf(ItemId) >= Target;

    public string Describe(FarmProgress progress) => $"item {ItemId} {progress.CountOf(ItemId)}/{Target}";
}

public sealed class ElapsedCondition(TimeSpan limit) : IStopCondition
{
    public TimeSpan Limit { get; } = limit;

    public bool IsMet(FarmProgress progress) => progress.Elapsed >= Limit;

    public string Describe(FarmProgress progress) =>
        $"elapsed {progress.Elapsed:hh\\:mm}/{Limit:hh\\:mm}";
}

public sealed class LevelCondition(int level) : IStopCondition
{
    public int Level { get; } = level;

    public bool IsMet(FarmProgress progress) => progress.Level >= Level;

    public string Describe(FarmProgress progress) => $"level {progress.Level}/{Level}";
}

public sealed class InventoryFullCondition : IStopCondition
{
    public bool IsMet(FarmProgress progress) => progress.InventoryFull;

    public string Describe(FarmProgress progress) =>
        progress.InventoryFull ? "inventory full" : "inventory has room";

    public bool IsSafety => true;
}

public sealed class DeathCondition : IStopCondition
{
    public bool IsMet(FarmProgress progress) => progress.Died;

    public string Describe(FarmProgress progress) => progress.Died ? "died" : "alive";

    public bool IsSafety => true;
}

public enum StopMode
{
    /// <summary>Stop as soon as any condition is met.</summary>
    Any,

    /// <summary>Keep going until every condition is met.</summary>
    All,
}

/// <summary>
/// A run's stop conditions, stacked. Targets compose, so "200 kills" and "30 of
/// item X" and "45 minutes" can all be set at once and the mode decides whether
/// the first or the last of them ends the run.
/// </summary>
public sealed class StopConditions(IReadOnlyList<IStopCondition> conditions, StopMode mode)
{
    public IReadOnlyList<IStopCondition> Conditions { get; } = conditions;

    public StopMode Mode { get; } = mode;

    public IReadOnlyList<IStopCondition> Met(FarmProgress progress) =>
        Conditions.Where(condition => condition.IsMet(progress)).ToList();

    /// <summary>
    /// The same ask, less what is already done, for picking a run back up.
    /// </summary>
    /// <remarks>
    /// Kills, items and time all subtract what the finished run banked; a
    /// target already met disappears rather than turning into an ask for zero.
    /// A level target is where the character stands, not something this run
    /// accumulated, so it rides along whole, and so do the safety conditions,
    /// which carry no state at all.
    /// </remarks>
    public StopConditions Remaining(FarmProgress progress)
    {
        var rest = new List<IStopCondition>();

        foreach (var condition in Conditions)
        {
            switch (condition)
            {
                case KillCountCondition kills:
                    if (kills.Target > progress.Kills)
                        rest.Add(new KillCountCondition(kills.Target - progress.Kills));
                    break;

                case MobKillCondition mob:
                    if (mob.Target > progress.KillsOf(mob.BNpcNameId))
                        rest.Add(new MobKillCondition(
                            mob.BNpcNameId, mob.Name, mob.Target - progress.KillsOf(mob.BNpcNameId)));
                    break;

                case ItemCountCondition item:
                    if (item.Target > progress.CountOf(item.ItemId))
                        rest.Add(new ItemCountCondition(
                            item.ItemId, item.Target - progress.CountOf(item.ItemId)));
                    break;

                case ElapsedCondition time:
                    if (time.Limit > progress.Elapsed)
                        rest.Add(new ElapsedCondition(time.Limit - progress.Elapsed));
                    break;

                default:
                    rest.Add(condition);
                    break;
            }
        }

        return new StopConditions(rest, Mode);
    }

    /// <summary>
    /// Whether anything here is a thing to reach rather than a way to come to
    /// harm.
    /// </summary>
    /// <remarks>
    /// A set of nothing but safety conditions never stops on its own, which is
    /// right for "run until I say otherwise" and wrong for what is left of a
    /// run that got everything it asked for and then died. Offering to pick
    /// that back up would start a run with no number to reach.
    /// </remarks>
    public bool Asking => Conditions.Any(condition => !condition.IsSafety);

    /// <summary>
    /// Every mob with a count of its own, whether or not it is filled.
    /// </summary>
    /// <remarks>
    /// A run counting mobs separately should go after exactly these and no
    /// others. The field it stands in can hold more kinds than the run has any
    /// reason to kill: a stop earlier in the same list may have finished one of
    /// them, and what is left after a death drops the mobs already done. Either
    /// way the ground still has them standing on it, and hunting one with no
    /// count behind it is killing without a number to reach.
    /// </remarks>
    public IReadOnlySet<uint> MobsCounted =>
        Conditions.OfType<MobKillCondition>().Select(mob => mob.BNpcNameId).ToHashSet();

    /// <summary>
    /// Mobs whose own count is already in, so killing another gains nothing.
    /// </summary>
    /// <remarks>
    /// A field can hold five hunting log entries' worth of mobs, and two of
    /// them come back faster than the other three. Left in the run's quarry
    /// once their count is full, the ground never looks empty, the circuit
    /// never moves on, and the run stands there killing what it already has
    /// enough of.
    ///
    /// Empty for everything that does not count mobs separately. An item run
    /// kills the whole field on purpose.
    /// </remarks>
    public IReadOnlySet<uint> MobsDone(FarmProgress progress) =>
        Conditions
            .OfType<MobKillCondition>()
            .Where(mob => mob.IsMet(progress))
            .Select(mob => mob.BNpcNameId)
            .ToHashSet();

    public bool ShouldStop(FarmProgress progress)
    {
        // An empty set means "run until I say otherwise". Without this, an
        // all-of set of nothing would be vacuously true and stop immediately.
        if (Conditions.Count == 0)
            return false;

        if (Conditions.Any(condition => condition.IsSafety && condition.IsMet(progress)))
            return true;

        var targets = Conditions.Where(condition => !condition.IsSafety).ToList();
        if (targets.Count == 0)
            return false;

        return Mode == StopMode.Any
            ? targets.Any(condition => condition.IsMet(progress))
            : targets.All(condition => condition.IsMet(progress));
    }
}
