using AutoKill.IPC;
using Dalamud.Plugin;

namespace AutoKill.Farming;

public enum RequirementState
{
    /// <summary>Present and working.</summary>
    Good,

    /// <summary>Missing or broken, and nothing will work without it.</summary>
    Blocking,

    /// <summary>Missing, but the run degrades rather than stops.</summary>
    Optional,
}

public sealed record Requirement(string Name, RequirementState State, string Detail);

/// <summary>
/// Whether the things this plugin leans on are actually there.
/// </summary>
/// <remarks>
/// Everything here is somebody else's plugin, discovered at runtime through IPC.
/// A missing one produces nothing but silence: no error, no exception, just a
/// character standing still or walking up to mobs without attacking. That is a
/// miserable thing to debug from the outside, so it is stated plainly instead.
///
/// Installed is checked separately from responding. A plugin can be installed
/// and unloaded, and the two want different advice: one is "enable it", the
/// other is "install it".
/// </remarks>
public sealed class Requirements(IDalamudPluginInterface plugin, NavmeshIpc navmesh, WrathIpc wrath)
{
    public IReadOnlyList<Requirement> All() => [Navmesh(), Wrath()];

    /// <summary>The reason a run cannot start, if there is one.</summary>
    public string? Blocker =>
        All().FirstOrDefault(r => r.State == RequirementState.Blocking) is { } bad
            ? $"{bad.Name}: {bad.Detail}"
            : null;

    private Requirement Navmesh()
    {
        const string name = "vnavmesh";

        if (Find(name) is not { } installed)
            return new Requirement(name, RequirementState.Blocking, "not installed, so nothing can move");

        if (!installed.IsLoaded)
            return new Requirement(name, RequirementState.Blocking, "installed but not enabled");

        if (!navmesh.Responding)
            return new Requirement(name, RequirementState.Blocking, "not answering");

        // Not being ready is ordinary rather than wrong: a mesh is built per
        // zone, and a run waits for it.
        if (!navmesh.Ready)
        {
            var progress = navmesh.BuildProgress;
            return new Requirement(
                name,
                RequirementState.Good,
                progress >= 0 ? $"building a mesh for this zone ({progress * 100:F0}%)" : "no mesh for this zone yet");
        }

        return new Requirement(name, RequirementState.Good, "ready");
    }

    private Requirement Wrath()
    {
        const string name = "Wrath Combo";

        if (Find("WrathCombo") is not { } installed)
            return new Requirement(name, RequirementState.Optional, "not installed, so the fighting is up to you");

        if (!installed.IsLoaded)
            return new Requirement(name, RequirementState.Optional, "installed but not enabled");

        // Auto-rotation being on says nothing about the job having anything to
        // rotate with, and a job with nothing in auto-mode stands there swinging
        // at nothing. Left alone means left alone, so this is said rather than
        // fixed behind somebody's back.
        if (wrath.Rotating && !wrath.JobReady)
        {
            return new Requirement(
                name,
                RequirementState.Optional,
                "auto-rotation is on, but this job has nothing enabled in auto-mode");
        }

        return wrath.Rotating
            ? new Requirement(name, RequirementState.Good, "auto-rotation already on, so it will be left alone")
            : new Requirement(name, RequirementState.Good, "ready, and it will set this job up if needed");
    }

    private IExposedPlugin? Find(string internalName) =>
        plugin.InstalledPlugins.FirstOrDefault(
            p => string.Equals(p.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
}
