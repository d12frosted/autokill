using System.Numerics;

namespace AutoKill.Core;

/// <summary>What is wrong with a quarry the run is getting nowhere with.</summary>
public enum QuarryTrouble
{
    None,

    /// <summary>
    /// The distance never closed. Either there is no way there at all, or
    /// something is in the way and the route keeps ending short of it.
    /// </summary>
    OutOfReach,

    /// <summary>
    /// Close enough to fight, and its health never moved. Usually nothing can
    /// see it: the rock that stopped the walk stops the spells too.
    /// </summary>
    OutOfSight,
}

/// <summary>What to do about the quarry just looked at.</summary>
/// <param name="GiveUp">
/// True once a second try has been had and changed nothing. Until then a stall
/// is worth answering by moving somewhere else rather than by walking away.
/// </param>
public readonly record struct QuarryCheck(QuarryTrouble Trouble, bool GiveUp)
{
    public static QuarryCheck Fine => new(QuarryTrouble.None, false);

    public bool Stalled => Trouble != QuarryTrouble.None;
}

/// <summary>
/// Noticing that a chosen quarry is going nowhere, and keeping a list of the
/// ones not worth choosing again yet.
/// </summary>
/// <remarks>
/// A run picks the nearest thing of the right kind, which is a straight line
/// measurement and says nothing about whether there is a way there. Across a
/// chasm, up a cliff, on a rock the mesh does not cover, or simply behind
/// something solid, the run heads for it forever: the route completes short of
/// it, or never starts, and picking again picks the same one.
///
/// Rather than working out why, this watches whether anything is happening.
/// Either the distance is coming down or its health is, and if neither has for
/// a while then whatever the reason, this is not a quarry worth standing on.
/// That covers no line of sight, no path, geometry snags and a rotation sitting
/// idle without needing to tell them apart, which is just as well, because from
/// the outside they look identical.
///
/// A first stall asks for a second try rather than an abandonment, since moving
/// somewhere else nearby is what clears most of these. There is only ever one
/// second try: re-arming it on any scrap of progress would let something that
/// inches forward and stops again hold the run there all day.
/// </remarks>
/// <param name="patience">How long nothing may happen before it counts as a stall.</param>
/// <param name="cooldown">How long one given up on stays passed over.</param>
/// <param name="wandered">
/// How far it has to move from where it was abandoned to be worth another look
/// straight away.
/// </param>
public sealed class QuarryWatch(TimeSpan patience, TimeSpan cooldown, float wandered = 8f)
{
    // Under half a yalm is standing still. A character holding position drifts
    // about this much, and drift is not getting closer.
    private const float Closer = 0.5f;

    private readonly Dictionary<ulong, Abandoned> abandoned = [];

    private ulong watching;
    private float closest;
    private uint weakest;
    private bool arrived;
    private bool triedAgain;
    private DateTime lastProgress;

    /// <summary>
    /// Take one look at the quarry currently chosen. Naming a different one
    /// starts over, so this is called every tick with whatever was picked.
    /// </summary>
    /// <param name="distance">Yalms from the character, however the caller measures that.</param>
    /// <param name="inRange">Whether the character is close enough to be attacking it.</param>
    public QuarryCheck Watch(
        ulong id, Vector3 position, float distance, uint hp, bool inRange, DateTime now)
    {
        if (id != watching)
        {
            watching = id;
            closest = distance;
            weakest = hp;
            arrived = inRange;
            triedAgain = false;
            lastProgress = now;
            return QuarryCheck.Fine;
        }

        // Having got there once, the walk is not what is failing, whatever it
        // does afterwards.
        arrived |= inRange;

        var closing = distance < closest - Closer;
        var hurting = hp < weakest;

        if (closing)
            closest = distance;
        if (hurting)
            weakest = hp;

        if (closing || hurting)
        {
            lastProgress = now;
            return QuarryCheck.Fine;
        }

        if (now - lastProgress < patience)
            return QuarryCheck.Fine;

        var trouble = arrived ? QuarryTrouble.OutOfSight : QuarryTrouble.OutOfReach;

        if (!triedAgain)
        {
            triedAgain = true;
            lastProgress = now;
            return new QuarryCheck(trouble, false);
        }

        abandoned[id] = new Abandoned(now, position);
        watching = 0;
        return new QuarryCheck(trouble, true);
    }

    /// <summary>
    /// Whether this one was given up on and is still not worth another try.
    /// </summary>
    /// <remarks>
    /// The list has to lapse. Mobs wander, respawns reuse ground, and the thing
    /// that was behind a rock walks out from behind it; a list that never
    /// forgot would empty a field of candidates over a long grind and leave the
    /// run with nothing to do in a place full of mobs.
    ///
    /// So an entry lapses with time, and at once if it has moved far enough
    /// that whatever was in the way probably is not any more.
    /// </remarks>
    public bool GivenUpOn(ulong id, Vector3 position, DateTime now)
    {
        if (!abandoned.TryGetValue(id, out var entry))
            return false;

        if (now - entry.When >= cooldown || Vector3.Distance(position, entry.Where) > wandered)
        {
            abandoned.Remove(id);
            return false;
        }

        return true;
    }

    private readonly record struct Abandoned(DateTime When, Vector3 Where);
}
