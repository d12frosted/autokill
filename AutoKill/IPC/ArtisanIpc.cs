using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace AutoKill.IPC;

/// <summary>The one thing Artisan is asked to do rather than only read from.</summary>
/// <remarks>
/// Crafting lists are read out of Artisan's configuration file, which keeps them
/// readable whether or not Artisan is loaded. This is the exception: a list
/// Artisan is holding in memory and has not written out cannot be read at all,
/// and the only thing that puts it on disk is Artisan saving its own config.
///
/// There is no call for "save yourself", so an ordinary setting call is used for
/// how it ends: every Change... endpoint assigns a value and then saves the whole
/// config. Handed back the value that is already in the file, it assigns nothing
/// new and saves everything, lists included.
///
/// Not while Artisan is busy. Another plugin may have a temporary override in
/// flight over the same setting, and the whole point of this is to be a save
/// rather than a change.
/// </remarks>
public sealed class ArtisanIpc(IDalamudPluginInterface plugin, IPluginLog log)
{
    private const string Name = "Artisan";

    /// <summary>
    /// Long enough to keep the plugin list off the draw path, short enough that
    /// enabling Artisan shows up while somebody is still looking at the window.
    /// </summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(1);

    private readonly ICallGateSubscriber<bool> isBusy =
        plugin.GetIpcSubscriber<bool>("Artisan.IsBusy");

    private readonly ICallGateSubscriber<bool> isListRunning =
        plugin.GetIpcSubscriber<bool>("Artisan.IsListRunning");

    // Standard rather than per-recipe, so it takes the value and nothing else,
    // and false for temporary because this is not meant to be put back later.
    private readonly ICallGateSubscriber<uint, bool, object> setMinimumSteps =
        plugin.GetIpcSubscriber<uint, bool, object>("Artisan.ChangeStandardMinimumStepsBeforeMiracle");

    private Version? version;
    private DateTime versionAsOf = DateTime.MinValue;

    /// <summary>Which Artisan is loaded, or nothing when none is.</summary>
    public Version? Version
    {
        get
        {
            if (DateTime.UtcNow - versionAsOf < CacheFor)
                return version;

            versionAsOf = DateTime.UtcNow;
            return version = plugin.InstalledPlugins
                .FirstOrDefault(other => other.InternalName == Name && other.IsLoaded)?
                .Version;
        }
    }

    /// <summary>
    /// Ask Artisan to write its configuration, by handing it back a setting it
    /// already has. Answers whether the call went through, not whether the file
    /// changed: only Artisan knows that, and the file being re-read is what says
    /// so afterwards.
    /// </summary>
    /// <param name="minimumStepsBeforeMiracle">
    /// The value read from Artisan's own file a moment ago. Passing anything
    /// else would be changing somebody's settings to work around a bug.
    /// </param>
    public bool Save(int minimumStepsBeforeMiracle)
    {
        if (Version is null)
            return false;

        if (Busy)
        {
            log.Information("Artisan is in the middle of something, so it was left alone.");
            return false;
        }

        try
        {
            setMinimumSteps.InvokeAction((uint)Math.Max(0, minimumStepsBeforeMiracle), false);
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"Artisan would not save its configuration: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Whether Artisan is crafting or running a list. Anything that cannot be
    /// answered counts as busy: the caller only ever uses this to decide whether
    /// to leave Artisan alone.
    /// </summary>
    private bool Busy =>
        Call(() => isBusy.InvokeFunc(), true) || Call(() => isListRunning.InvokeFunc(), true);

    private T Call<T>(Func<T> call, T fallback)
    {
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            log.Warning($"Artisan call failed: {ex.Message}");
            return fallback;
        }
    }
}
