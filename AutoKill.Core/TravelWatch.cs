namespace AutoKill.Core;

/// <summary>What to do about the spot currently being travelled to.</summary>
/// <param name="GiveUp">
/// True once a fresh route has been asked for and changed nothing. The caller
/// should go somewhere else on the circuit.
/// </param>
public readonly record struct TravelCheck(bool Stalled, bool GiveUp)
{
    public static TravelCheck Fine => new(false, false);
}

/// <summary>
/// Noticing that the journey to a spot is going nowhere, and keeping a list of
/// the spots not worth setting off for yet.
/// </summary>
/// <remarks>
/// Spawn positions carry no height, so a spot is an X and a Z dropped onto
/// whatever the mesh says is underneath. Sometimes there is nothing sensible
/// underneath: the point lands inside a cliff, on a ledge with no way up, under
/// a roof no route climbs to, or out over water. The route then ends short of
/// it and the run stands there, or it is refused outright and the run asks again
/// every couple of seconds for as long as it lasts.
///
/// Same question as the quarry watch, and the same answer: rather than working
/// out why, watch whether the distance is coming down, and if it is not then
/// this is not a spot worth walking to whatever the reason.
///
/// The patience is longer than the quarry's. Working out a route across a large
/// zone takes a moment, and standing still waiting for one is not the same as
/// being unable to get there.
/// </remarks>
/// <param name="patience">How long the distance may sit still before it counts as a stall.</param>
/// <param name="cooldown">
/// How long a spot given up on stays passed over. It lapses because the reason
/// is not always the spot: a mesh still loading, or an approach from the far
/// side of the field, can turn one that would not path into one that will.
/// </param>
public sealed class TravelWatch(TimeSpan patience, TimeSpan cooldown)
{
    // Under half a yalm is standing still, the same as it is for a quarry.
    private const float Closer = 0.5f;

    private readonly Dictionary<int, DateTime> abandoned = [];

    private int watching = -1;
    private float closest;
    private bool triedAgain;
    private DateTime lastProgress;

    /// <summary>
    /// Take one look at the journey in hand. Called every tick that the run is
    /// actually trying to get somewhere.
    /// </summary>
    public TravelCheck Watch(int spot, float distance, DateTime now)
    {
        if (spot != watching)
        {
            watching = spot;
            closest = distance;
            triedAgain = false;
            lastProgress = now;
            return TravelCheck.Fine;
        }

        if (distance < closest - Closer)
        {
            closest = distance;
            lastProgress = now;
            return TravelCheck.Fine;
        }

        if (now - lastProgress < patience)
            return TravelCheck.Fine;

        if (!triedAgain)
        {
            triedAgain = true;
            lastProgress = now;
            return new TravelCheck(true, false);
        }

        abandoned[spot] = now;
        watching = -1;
        return new TravelCheck(true, true);
    }

    /// <summary>
    /// Setting off on a fresh leg. A circuit travels to the same spot over and
    /// over, so a clock left running between legs would trip on the first sample
    /// of the next one.
    /// </summary>
    public void SetOff() => watching = -1;

    /// <summary>Whether this spot was given up on and is still not worth setting off for.</summary>
    public bool GivenUpOn(int spot, DateTime now)
    {
        if (!abandoned.TryGetValue(spot, out var when))
            return false;

        if (now - when >= cooldown)
        {
            abandoned.Remove(spot);
            return false;
        }

        return true;
    }
}
