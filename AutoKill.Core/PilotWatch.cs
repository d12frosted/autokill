using System.Numerics;

namespace AutoKill.Core;

/// <summary>
/// Noticing that the player has taken the controls.
/// </summary>
/// <remarks>
/// The run steers the character through vnavmesh, so any tick can say whether
/// the movement on screen is the run's own doing. Movement that is neither ours
/// nor otherwise accounted for is somebody at the keyboard, and fighting them
/// for the character is the one thing a run must never do.
///
/// Watched as ground covered rather than keys pressed. Keys can be rebound and
/// a gamepad has none, but a character that walked away from where it was
/// standing walked away no matter what moved it.
///
/// The anchor follows every sample that is steered or excused: where the run
/// put the character, or where a knockback threw it, is the new place it is
/// standing, not somewhere it walked from. Height never counts, because a
/// dismount in the air is followed by a fall and nobody pressed anything.
/// </remarks>
/// <param name="tolerance">
/// How far the character may stand from its anchor before that reads as input.
/// Wide enough for mount hover and the slide after a route stops, and a fraction
/// of a second of deliberate walking crosses it.
/// </param>
public sealed class PilotWatch(float tolerance)
{
    private Vector2? anchor;

    /// <summary>
    /// One look at where the character is standing and who put it there.
    /// </summary>
    /// <param name="steered">Whether the run is moving the character right now.</param>
    /// <param name="excused">
    /// Whether movement would mean nothing at the moment: fighting, casting,
    /// loading, or anything else that shifts a character with nobody driving.
    /// </param>
    /// <returns>True when the player has walked the character away.</returns>
    public bool Check(Vector3 position, bool steered, bool excused)
    {
        var here = new Vector2(position.X, position.Z);

        if (steered || excused || anchor is null)
        {
            anchor = here;
            return false;
        }

        return Vector2.Distance(here, anchor.Value) > tolerance;
    }

    /// <summary>
    /// Start from wherever the character stands now. Called on resume and on a
    /// change of zone, where the old ground means nothing.
    /// </summary>
    public void Reset() => anchor = null;
}
