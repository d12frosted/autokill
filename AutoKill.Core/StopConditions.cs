namespace AutoKill.Core;

/// <summary>Everything a stop condition is allowed to look at.</summary>
public readonly record struct FarmProgress(
    int Kills,
    TimeSpan Elapsed,
    int Level,
    bool InventoryFull,
    bool Died,
    IReadOnlyDictionary<uint, int> ItemsGained)
{
    public int CountOf(uint itemId) => ItemsGained.GetValueOrDefault(itemId);
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
