using System.Numerics;

namespace AutoKill.Core;

/// <summary>One thing of ours standing in the field, as the watch needs it.</summary>
public readonly record struct FieldMob(ulong Id, uint NameId, Vector3 Position);

/// <summary>A spawn point coming back, timed from a death that was watched.</summary>
/// <param name="Id">Whatever is standing there now, for the record of it.</param>
public readonly record struct Respawn(ulong Id, uint NameId, TimeSpan Took);

/// <summary>
/// Timing single spawn points while farming over them.
/// </summary>
/// <remarks>
/// The other two measurements both wait for a spot to be empty. Leaving one and
/// coming back carries the whole trip inside it and is thrown away past ten
/// minutes, which a wide circuit never beats; standing on one and watching it
/// only closes if something pops within the few seconds of patience before the
/// run moves on. Between them they miss the case that matters most, which is a
/// field busy enough that it is never empty and never left: hundreds of kills
/// and nothing learnt.
///
/// A spawn point does not need the spot around it to be quiet. One thing went
/// down here, and later something else was standing here: that gap is the
/// respawn, with no travel and no waiting in it, and a run fighting through a
/// crowded field produces one every time anything comes back.
///
/// Timing runs from where a mob lived rather than from where it fell. A pull
/// walks it in to wherever the character is standing, sometimes tens of yalms
/// off, and the ground it dies on belongs to nothing. Where it was first seen
/// is close enough to its spawn point to match what appears there next.
///
/// What is watched has to stay watched. Something dropping out of the table far
/// away is a draw distance rather than a death, a death left behind when the
/// circuit moves on comes back where nobody is looking, and a gap in the looking
/// at all, a loading screen or a run stopped and restarted, could put any amount
/// of time inside a measurement. All three throw the measurement away rather
/// than guess at it.
/// </remarks>
/// <param name="watching">
/// How far a piece of ground can be and still count as watched, both for a
/// death happening on it and for staying interested in it afterwards. Set to
/// the range a run already trusts the object table to be complete over, since
/// beyond that a thing missing from the table means nothing either way.
/// </param>
/// <param name="samePlace">
/// How close what stands up has to be to where the last one lived to be the
/// same spawn point coming back.
/// </param>
/// <param name="remembered">How long a death is worth waiting on an answer to.</param>
/// <param name="blind">
/// A gap between looks long enough that the field cannot be vouched for any
/// more.
/// </param>
public sealed class FieldWatch(
    float watching = 90f,
    float samePlace = 12f,
    TimeSpan? remembered = null,
    TimeSpan? blind = null)
{
    private readonly TimeSpan remember = remembered ?? TimeSpan.FromMinutes(10);
    private readonly TimeSpan gap = blind ?? TimeSpan.FromSeconds(15);

    private readonly Dictionary<ulong, Alive> alive = [];
    private readonly List<Gone> gone = [];

    private DateTime lastLook = DateTime.MinValue;

    /// <summary>
    /// One look at everything of ours in the world, from where the character is
    /// standing. Gives back whatever spawn points that look closed.
    /// </summary>
    /// <param name="here">Where the character is, since watching is done from there.</param>
    /// <param name="standing">
    /// Everything of the kinds the run cares about that is up and alive, at
    /// whatever range: a thing beyond the watched ground is not timed, but it is
    /// still not missing.
    /// </param>
    public IReadOnlyList<Respawn> Look(DateTime now, Vector3 here, IReadOnlyList<FieldMob> standing)
    {
        // Not looking is not the same as nothing being there, and the difference
        // is the whole measurement.
        if (now - lastLook > gap)
            Left();

        lastLook = now;

        var present = standing.Select(mob => mob.Id).ToHashSet();

        foreach (var (id, was) in alive.ToList())
        {
            if (present.Contains(id))
                continue;

            alive.Remove(id);

            // Out of sight rather than out of the world.
            if (Vector3.Distance(was.Home, here) <= watching)
                gone.Add(new Gone(id, was.NameId, was.Home, now));
        }

        var closed = new List<Respawn>();

        foreach (var mob in standing)
        {
            if (alive.ContainsKey(mob.Id))
                continue;

            // First sight of it, so this is where it lives as far as we can
            // tell: either it stood up here a moment ago or it was here when we
            // arrived, and mobs at rest keep near their own ground.
            alive[mob.Id] = new Alive(mob.NameId, mob.Position);

            if (Answer(mob, now) is { } answer)
                closed.Add(answer);
        }

        gone.RemoveAll(death =>
            now - death.When > remember || Vector3.Distance(death.Home, here) > watching);

        return closed;
    }

    /// <summary>
    /// Stop vouching for any of it. Called when the run leaves the zone or the
    /// field, since everything below rests on having been there the whole time.
    /// </summary>
    public void Left()
    {
        alive.Clear();
        gone.Clear();
    }

    /// <summary>
    /// Which death, if any, this one standing up answers. The nearest, since
    /// two spawn points close enough to be confused are still both spawn points
    /// and pairing them the wrong way round measures the same pair of gaps.
    /// </summary>
    private Respawn? Answer(FieldMob mob, DateTime now)
    {
        var best = -1;
        var nearest = float.MaxValue;

        for (var i = 0; i < gone.Count; i++)
        {
            var death = gone[i];

            // The same creature back in the table is the same creature. It
            // never died, so there is nothing to time, and the death it was
            // filed under was never one.
            if (death.Id == mob.Id)
            {
                gone.RemoveAt(i);
                return null;
            }

            if (death.NameId != mob.NameId)
                continue;

            var away = Vector3.Distance(death.Home, mob.Position);
            if (away > samePlace || away >= nearest)
                continue;

            nearest = away;
            best = i;
        }

        if (best < 0)
            return null;

        var answered = gone[best];
        gone.RemoveAt(best);

        return new Respawn(mob.Id, mob.NameId, now - answered.When);
    }

    private readonly record struct Alive(uint NameId, Vector3 Home);

    private readonly record struct Gone(ulong Id, uint NameId, Vector3 Home, DateTime When);
}
