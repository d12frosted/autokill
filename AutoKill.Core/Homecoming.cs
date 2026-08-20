namespace AutoKill.Core;

/// <summary>What the trip home should do this tick.</summary>
public enum HomeStep
{
    Wait,
    Go,
    GiveUp,
}

/// <summary>
/// The timing of a teleport back to where a run set off from.
/// </summary>
/// <remarks>
/// A finished run rarely ends somewhere a teleport can be cast that instant:
/// the last kill leaves combat trailing, and the game refuses the cast until
/// it settles. So the trip is a patience and a retry rather than one attempt:
/// wait while the character is busy, cast when it is not, and leave a decent
/// gap between casts so a teleport already leaving is not cancelled by the
/// next ask.
///
/// It gives up rather than lingering. A character stuck busy, or a cast
/// refused over and over, means the moment has passed, and a surprise teleport
/// half a minute after the run ended is worse than staying put.
/// </remarks>
/// <param name="patience">How long the whole trip may take before it is abandoned.</param>
/// <param name="retry">The gap between casts, longer than a cast takes to leave.</param>
public sealed class Homecoming(TimeSpan patience, TimeSpan retry)
{
    private DateTime? since;
    private DateTime lastGo = DateTime.MinValue;

    /// <summary>
    /// One look at whether to cast, keep waiting, or stop trying.
    /// </summary>
    /// <param name="busy">
    /// Whether the character could not cast right now: fighting, already
    /// casting, dead, or mid-load.
    /// </param>
    public HomeStep Check(bool busy, DateTime now)
    {
        since ??= now;

        if (now - since >= patience)
            return HomeStep.GiveUp;

        if (busy || now - lastGo < retry)
            return HomeStep.Wait;

        lastGo = now;
        return HomeStep.Go;
    }
}
