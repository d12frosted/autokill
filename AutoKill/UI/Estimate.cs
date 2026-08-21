using AutoKill.Core;

namespace AutoKill.UI;

/// <summary>
/// Saying how long something has left, in the two places a goal is shown: the
/// run screen and the overlay beside the game.
/// </summary>
internal static class Estimate
{
    /// <summary>
    /// "12 / 30", with how much longer the rest should take at the pace shown
    /// so far. Nothing extra when there is no pace to go on: a made-up number
    /// would sit on the bar looking like a fact.
    /// </summary>
    public static string? Reads(int done, int target, TimeSpan elapsed) =>
        Pace.TimeToGo(done, target, elapsed) is { } toGo && toGo > TimeSpan.Zero
            ? $"{done} / {target}   ~{Pace.Roughly(toGo)}"
            : null;
}
