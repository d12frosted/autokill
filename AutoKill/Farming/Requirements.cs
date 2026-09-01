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
public sealed class Requirements(
    IDalamudPluginInterface plugin,
    NavmeshIpc navmesh,
    WrathIpc wrath,
    BossModIpc bossmod,
    LifestreamIpc lifestream)
{
    public IReadOnlyList<Requirement> All() => [Navmesh(), Wrath(), BossMod(), Lifestream()];

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

        return wrath.Rotating
            ? new Requirement(name, RequirementState.Good, "auto-rotation is on, and a run takes it over and hands it back")
            : new Requirement(name, RequirementState.Good, "ready, and it will set this job up if needed");
    }

    private Requirement BossMod()
    {
        const string only = "so a run stands in whatever is cast at it";

        // The two are forks of each other and answer on the same IPC names, so
        // which one is here decides nothing but what to call it.
        var installed = Find("BossMod") ?? Find("BossModReborn");

        if (installed is null)
            return new Requirement("BossMod", RequirementState.Optional, $"not installed, {only}");

        if (!installed.IsLoaded)
            return new Requirement(installed.Name, RequirementState.Optional, $"installed but not enabled, {only}");

        if (!bossmod.Responding)
            return new Requirement(installed.Name, RequirementState.Optional, $"not answering, {only}");

        return new Requirement(installed.Name, RequirementState.Good, "ready, and a fight steps out of what it sees coming");
    }

    private Requirement Lifestream()
    {
        const string name = "Lifestream";

        // One zone in the game has no aetheryte standing in it, and the
        // aethernet hop that gets into it is the only thing this is for. Worth
        // naming rather than leaving as a run that stops for no visible reason
        // in the one place it cannot reach.
        const string only = "so the Dravanian Hinterlands cannot be reached";

        if (Find(name) is not { } installed)
            return new Requirement(name, RequirementState.Optional, $"not installed, {only}");

        if (!installed.IsLoaded)
            return new Requirement(name, RequirementState.Optional, $"installed but not enabled, {only}");

        if (!lifestream.Responding)
            return new Requirement(name, RequirementState.Optional, $"not answering, {only}");

        return new Requirement(name, RequirementState.Good, "ready, for the aethernet hop into the Hinterlands");
    }

    private IExposedPlugin? Find(string internalName) =>
        plugin.InstalledPlugins.FirstOrDefault(
            p => string.Equals(p.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
}
