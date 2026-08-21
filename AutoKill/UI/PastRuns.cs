using AutoKill.Core;
using AutoKill.Data;

namespace AutoKill.UI;

/// <summary>
/// What every past run over a piece of ground came to, pooled.
/// </summary>
/// <remarks>
/// One run is not a pace: a trip that hit a bad respawn stretch, or one that
/// walked into a crowd, says more about that half hour than about the field.
/// Pooling every run filed over the same ground is the closest thing to an
/// honest rate the window has before a fresh run has produced one of its own.
///
/// Worked out once per target rather than per frame, since a progress bar asks
/// this sixty times a second and the answer only changes when a run is filed.
/// </remarks>
public sealed class PastRuns(RunHistory history)
{
    private FarmTarget? cachedFor;
    private int cachedRecords = -1;
    private TimeSpan spent;
    private int kills;
    private readonly Dictionary<uint, int> gained = [];

    /// <summary>Whether anything here has been farmed long enough to say.</summary>
    public bool Anything(FarmTarget target)
    {
        Refresh(target);
        return spent > TimeSpan.Zero;
    }

    /// <summary>How fast this ground has given up kills, and over how much farming.</summary>
    public KnownPace? KillsPace(FarmTarget target)
    {
        Refresh(target);
        return Pace.PerHour(kills, spent) is { } perHour ? new KnownPace(perHour, spent) : null;
    }

    /// <summary>The same for one thing it drops.</summary>
    public KnownPace? PaceOf(FarmTarget target, uint itemId)
    {
        Refresh(target);
        return Pace.PerHour(gained.GetValueOrDefault(itemId), spent) is { } perHour
            ? new KnownPace(perHour, spent)
            : null;
    }

    private void Refresh(FarmTarget target)
    {
        if (ReferenceEquals(cachedFor, target) && cachedRecords == history.Records.Count)
            return;

        cachedFor = target;
        cachedRecords = history.Records.Count;
        kills = 0;
        spent = TimeSpan.Zero;
        gained.Clear();

        foreach (var run in history.Records)
        {
            if (run.TerritoryId != target.Area.TerritoryTypeId)
                continue;
            if (!run.Mobs.Intersect(target.BNpcNameIds).Any())
                continue;

            kills += run.Kills;
            spent += TimeSpan.FromSeconds(run.ElapsedSeconds);

            foreach (var (itemId, count) in run.Gained)
                gained[itemId] = gained.GetValueOrDefault(itemId) + count;
        }
    }
}
