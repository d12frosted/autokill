using AutoKill.Core;
using AutoKill.Farming;
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
    /// The chat sound to play when a run ends, 1 to 16 as in &lt;se.1&gt;, or
    /// zero for silence. Off by default: a sound nobody asked for is noise.
    /// </summary>
    public int FinishSound { get; set; }

    /// <summary>
    /// Write a trace of each run to disk, for working out afterwards where the
    /// time actually went.
    /// </summary>
    public bool RecordRuns { get; set; }

    /// <summary>
    /// What to do when the character cannot fight what was picked: a crafter, or
    /// a battle job that has not got far enough up.
    /// </summary>
    /// <remarks>
    /// Switching by default because the plugin already teleports, mounts and
    /// fights on its own, and refusing to farm because the wrong gearset happens
    /// to be on would be the one thing it made you do by hand.
    /// </remarks>
    public JobPolicy JobPolicy { get; set; } = JobPolicy.Switch;

    /// <summary>
    /// The ClassJob to reach for when changing, or zero for whatever suits the
    /// field best.
    /// </summary>
    /// <remarks>
    /// The job rather than the gearset. Two gearsets for one job are the same
    /// answer to "what should I go as", and picking between them would be a
    /// preference about gear rather than about the job.
    ///
    /// A named job that cannot manage the field is passed over rather than sent
    /// there to die, so this is a preference and not an instruction.
    /// </remarks>
    public uint PreferredJob { get; set; }

    /// <summary>
    /// Teleport back to the zone a run set off from, once it ends on its own.
    /// A run stopped by hand stays put: whoever pressed the button is standing
    /// right there and can decide for themselves.
    /// </summary>
    public bool ReturnWhenDone { get; set; }

    /// <summary>Keep the chocobo companion out while farming.</summary>
    public bool SummonCompanion { get; set; }

    /// <summary>How the chocobo should behave once it is out.</summary>
    public ChocoboStance CompanionStance { get; set; } = ChocoboStance.Attacker;
}
