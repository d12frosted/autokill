using Dalamud.Configuration;

namespace AutoKill;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>
    /// How far away a destination has to be before it is worth mounting, and
    /// flying if the zone allows it.
    /// </summary>
    /// <remarks>
    /// Taste, not physics. A mount costs a couple of seconds to summon and pays
    /// that back over distance, but where the balance sits depends on how much
    /// somebody minds walking. Low values mount for almost every hop between
    /// spots; high values keep it on foot inside an area.
    /// </remarks>
    public float MountDistance { get; set; } = 25f;

    /// <summary>How long an emptied spot is given before moving on round the circuit.</summary>
    public float RespawnPatienceSeconds { get; set; } = 6f;

    /// <summary>Announce starts and finishes in chat and as a toast.</summary>
    public bool Notifications { get; set; } = true;

    /// <summary>
    /// Write a trace of each run to disk, for working out afterwards where the
    /// time actually went.
    /// </summary>
    public bool RecordRuns { get; set; }
}
