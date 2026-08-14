using AutoKill.Core;
using AutoKill.Data;
using AutoKill.IPC;
using Dalamud.Plugin.Services;

namespace AutoKill.Farming;

/// <summary>Owns the running session and ticks it.</summary>
public sealed class FarmController : IDisposable
{
    private readonly IFramework framework;
    private readonly NavmeshIpc navmesh;
    private readonly WrathIpc wrath;
    private readonly IClientState clientState;
    private readonly IObjectTable objects;
    private readonly ITargetManager targets;
    private readonly IDataManager data;
    private readonly IPluginLog log;

    public FarmController(
        IFramework framework,
        NavmeshIpc navmesh,
        WrathIpc wrath,
        IClientState clientState,
        IObjectTable objects,
        ITargetManager targets,
        IDataManager data,
        IPluginLog log)
    {
        this.framework = framework;
        this.navmesh = navmesh;
        this.wrath = wrath;
        this.clientState = clientState;
        this.objects = objects;
        this.targets = targets;
        this.data = data;
        this.log = log;

        framework.Update += OnUpdate;
    }

    public FarmSession? Current { get; private set; }

    public bool Running => Current is { Phase: not FarmPhase.Finished };

    public string? Blocker =>
        !navmesh.Available ? "vnavmesh is not responding, so nothing can move." : null;

    public void Start(MobEntry mob, FarmLocation location, StopConditions conditions)
    {
        Stop("replaced");
        Current = new FarmSession(
            mob, location, conditions, navmesh, wrath, clientState, objects, targets, data, log);
        log.Information($"Farming {mob.Name} in {location.ZoneName}.");
    }

    public void Stop(string reason = "stopped")
    {
        Current?.Finish(reason);
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        Stop("plugin unloading");
    }

    private void OnUpdate(IFramework _)
    {
        if (Current is not { Phase: not FarmPhase.Finished } session)
            return;

        try
        {
            session.Tick();
        }
        catch (Exception ex)
        {
            log.Error(ex, "Farming loop threw, stopping.");
            session.Finish("something went wrong, see the log");
        }
    }
}
