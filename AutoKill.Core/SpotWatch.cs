namespace AutoKill.Core;

/// <summary>
/// Watching one spot empty and fill again, while standing on it.
/// </summary>
/// <remarks>
/// How long a place takes to come back was measured the only way a circuit
/// offers: stamp a spot on leaving it, and close the loop on returning. That
/// number carries the whole trip back inside it, and it exists at all only when
/// the run does return, in the same session, inside the ten minutes past which
/// a gap is thrown away as untrustworthy. A field of thirty spots never returns
/// that quickly, and a mob with one spot never leaves at all, so the places
/// farmed hardest were the ones that learnt nothing.
///
/// Standing still and watching measures the same thing directly. There is no
/// travel in it, and the case it serves best is the one that used to record
/// nothing: one spot, cleared, waited on.
///
/// It only counts what we emptied ourselves. A spot that was already quiet when
/// we arrived may have been quiet for an hour, and timing from our arrival
/// would be timing our walk.
/// </remarks>
public sealed class SpotWatch
{
    private bool ours;
    private DateTime? emptySince;

    /// <summary>
    /// Something is standing here. Gives back how long it took to come back,
    /// when this watch is the one that cleared the spot, and nothing otherwise.
    /// </summary>
    public TimeSpan? Occupied(DateTime now)
    {
        var took = ours && emptySince is { } since ? now - since : (TimeSpan?)null;

        ours = true;
        emptySince = null;

        return took;
    }

    /// <summary>Nothing is standing here.</summary>
    public void Empty(DateTime now)
    {
        if (ours)
            emptySince ??= now;
    }

    /// <summary>Moved on. What happens where nobody is looking is not observed.</summary>
    public void Left()
    {
        ours = false;
        emptySince = null;
    }
}
