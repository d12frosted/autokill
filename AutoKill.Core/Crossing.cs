namespace AutoKill.Core;

/// <summary>What the crossing should do this tick.</summary>
public enum CrossingStep
{
    Wait,
    Go,
    GiveUp,
}

/// <summary>
/// The timing of an aethernet hop out of the city a teleport landed in and into
/// the zone the run is actually for.
/// </summary>
/// <remarks>
/// One zone in the game has no aetheryte of its own: the Dravanian Hinterlands
/// is reached by teleporting to Idyllshire and taking the aethernet to one of
/// its gates, which are shards standing in the Hinterlands rather than in the
/// town. So the hop is the zone transition, not a walk through a gate.
///
/// It is a patience and a retry for the same reason the trip home is. Landing
/// is a loading screen, the aethernet is a menu that cannot be opened until
/// that has finished, and nothing reports back: a hop that did nothing looks
/// exactly like a hop still going. So the ask is repeated at a gap wide enough
/// for one to have played out, and each ask moves on to the next gate, since
/// the likeliest reason for a gate doing nothing is that it was never attuned.
///
/// The gates cycle rather than running out. A first ask eaten by a loading
/// screen deserves a second go, and every gate lands in the same zone, so
/// there is nothing to be gained by ruling one out for good.
/// </remarks>
/// <param name="gates">
/// The aethernet shards standing in the target zone, in the order to try them.
/// </param>
/// <param name="patience">How long to keep trying before the run gives up on the zone.</param>
/// <param name="retry">The gap between asks, wider than a hop takes to land.</param>
public sealed class Crossing(IReadOnlyList<uint> gates, TimeSpan patience, TimeSpan retry)
{
    private DateTime? since;
    private DateTime lastGo = DateTime.MinValue;
    private int next;

    /// <summary>The gate the last <see cref="CrossingStep.Go"/> was for.</summary>
    public uint Gate { get; private set; }

    /// <summary>
    /// One look at whether to hop, keep waiting, or stop trying.
    /// </summary>
    /// <param name="busy">
    /// Whether the character could not use the aethernet right now: mid-load,
    /// in a cutscene, dead, or already being moved by something else.
    /// </param>
    public CrossingStep Check(bool busy, DateTime now)
    {
        if (gates.Count == 0)
            return CrossingStep.GiveUp;

        since ??= now;

        if (now - since >= patience)
            return CrossingStep.GiveUp;

        if (busy || now - lastGo < retry)
            return CrossingStep.Wait;

        lastGo = now;
        Gate = gates[next];
        next = (next + 1) % gates.Count;
        return CrossingStep.Go;
    }
}
