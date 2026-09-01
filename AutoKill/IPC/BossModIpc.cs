using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace AutoKill.IPC;

/// <summary>Stepping out of things, delegated to BossMod.</summary>
/// <remarks>
/// BossMod and BossMod Reborn are forks of each other and answer on the same
/// IPC names, so there is one integration and whichever of them is loaded picks
/// it up. Nothing here needs to know which.
///
/// What is borrowed is a preset. BossMod's autorotation is a set of modules with
/// tracks, and one of those modules, NormalMovement, is the character's feet.
/// Turned to Pathfind it walks a route round every zone BossMod believes is
/// about to be dangerous, and out in a field those come from the casts
/// themselves rather than from a boss module: BossMod reads a shape off the
/// action and puts a zone under it. That is the whole of what this plugin wants.
/// The rotation stays with Wrath, because the preset carries nothing that swings.
///
/// The preset is activated only while the run is standing in a fight, and what
/// the player had active is put back when it is not. Activating one clears
/// whatever was running, so remembering it is the only way to hand it back.
/// </remarks>
public sealed class BossModIpc : IDisposable
{
    /// <summary>
    /// Named for this plugin because it turns up in somebody's preset list
    /// under whatever it is called, and an unexplained name there is worse than
    /// an obvious one.
    /// </summary>
    private const string PresetName = "AutoKill";

    /// <summary>
    /// One module and two tracks. Pathfind is the dodging. MaxRange keeps the
    /// character at the far edge of its own reach rather than walking a caster
    /// into melee to stand in things, which is also how a person plays.
    /// </summary>
    private const string Preset = """
        {
          "Name": "AutoKill",
          "Modules": {
            "BossMod.Autorotation.MiscAI.NormalMovement": [
              { "Track": "Destination", "Option": "Pathfind" },
              { "Track": "Range", "Option": "MaxRange" }
            ]
          }
        }
        """;

    /// <summary>
    /// How often it is worth asking again after BossMod says no. Handing over is
    /// reconciled on every tick, and hammering a refusal helps nobody.
    /// </summary>
    private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(5);

    private readonly IPluginLog log;

    private readonly ICallGateSubscriber<string, bool, bool> create;
    private readonly ICallGateSubscriber<string?> getActive;
    private readonly ICallGateSubscriber<string, bool> setActive;
    private readonly ICallGateSubscriber<bool> clearActive;
    private readonly ICallGateSubscriber<string, bool> delete;

    private bool made;
    private string? restore;
    private DateTime lastAttempt = DateTime.MinValue;

    public BossModIpc(IDalamudPluginInterface plugin, IPluginLog log)
    {
        this.log = log;

        create = plugin.GetIpcSubscriber<string, bool, bool>("BossMod.Presets.Create");
        getActive = plugin.GetIpcSubscriber<string?>("BossMod.Presets.GetActive");
        setActive = plugin.GetIpcSubscriber<string, bool>("BossMod.Presets.SetActive");
        clearActive = plugin.GetIpcSubscriber<bool>("BossMod.Presets.ClearActive");
        delete = plugin.GetIpcSubscriber<string, bool>("BossMod.Presets.Delete");
    }

    /// <summary>Whether BossMod answers at all.</summary>
    public bool Responding
    {
        get
        {
            try
            {
                getActive.InvokeFunc();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Whether BossMod is moving the character right now, which is the answer to
    /// "who walked it over there" for everything else that watches the feet.
    /// </summary>
    public bool Driving { get; private set; }

    /// <summary>
    /// Hand the feet over. Cheap once it has been done, so it can be asked for
    /// on every tick of a fight.
    /// </summary>
    public bool Dodge()
    {
        if (Driving)
            return true;

        if (DateTime.UtcNow - lastAttempt < RetryEvery)
            return false;

        lastAttempt = DateTime.UtcNow;

        if (!made)
        {
            if (!Try(() => create.InvokeFunc(Preset, true), false))
            {
                log.Warning("BossMod would not take the preset, so it will not be stepping out of anything.");
                return false;
            }

            made = true;
        }

        // Read before activating, since activating is what clears it. Ours
        // being active already is not something to hand back to.
        restore = Try<string?>(() => getActive.InvokeFunc(), null);
        if (restore == PresetName)
            restore = null;

        if (!Try(() => setActive.InvokeFunc(PresetName), false))
        {
            log.Warning("BossMod would not activate the preset, so it will not be stepping out of anything.");
            restore = null;
            return false;
        }

        Driving = true;
        return true;
    }

    /// <summary>Give the feet back, and whatever preset was running with them.</summary>
    public void Release()
    {
        if (!Driving)
            return;

        Driving = false;
        lastAttempt = DateTime.MinValue;

        if (restore is { } theirs)
            Try(() => setActive.InvokeFunc(theirs), false);
        else
            Try(() => clearActive.InvokeFunc(), false);

        restore = null;
    }

    public void Dispose()
    {
        Release();

        // Leaving a preset behind in somebody's list is a lasting change to
        // their configuration, which nothing else this plugin borrows makes.
        if (made)
            Try(() => delete.InvokeFunc(PresetName), false);
    }

    private T Try<T>(Func<T> call, T fallback)
    {
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            log.Warning($"BossMod call failed: {ex.Message}");
            return fallback;
        }
    }
}
