using AutoKill.Core;

namespace AutoKill.UI;

/// <summary>
/// Saying how long a goal has left, in the two places one is shown: the run
/// screen and the overlay beside the game.
/// </summary>
internal static class Estimate
{
    /// <summary>
    /// "12 / 30", with how much longer the rest should take.
    /// </summary>
    /// <remarks>
    /// What past runs over this ground gave and what this one is giving are
    /// weighed against each other, so a bar has an answer from the first
    /// second and that answer does not lurch about while the run is young.
    /// With neither, nothing is said: a made-up number would sit on the bar
    /// looking like a fact.
    /// </remarks>
    public static string? Reads(int done, int target, TimeSpan elapsed, KnownPace? known = null) =>
        Pace.TimeToGo(done, target, elapsed, known) is { } left && left > TimeSpan.Zero
            ? $"{done} / {target}   ~{Pace.Roughly(left)}"
            : null;
}
