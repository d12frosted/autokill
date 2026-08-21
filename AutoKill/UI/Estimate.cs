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
    /// The run's own pace is used the moment it has one, since nothing knows
    /// this field today better than the last few minutes on it. Before that
    /// there is a whole first minute with a bar and no answer, which is exactly
    /// when somebody is watching, so past runs over the same ground fill the
    /// gap. With neither, nothing is said: a made-up number would sit on the
    /// bar looking like a fact.
    /// </remarks>
    public static string? Reads(int done, int target, TimeSpan elapsed, double? beforeThat = null)
    {
        var toGo = Pace.TimeToGo(done, target, elapsed)
                   ?? Pace.TimeFor(target - done, beforeThat);

        return toGo is { } left && left > TimeSpan.Zero
            ? $"{done} / {target}   ~{Pace.Roughly(left)}"
            : null;
    }
}
