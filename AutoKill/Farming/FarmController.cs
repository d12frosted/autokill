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
    private readonly ICondition condition;
    private readonly Notifier notifier;
    private readonly Func<uint, string> itemName;
    private readonly Configuration config;
    private readonly Func<RunRecorder?> newRecorder;
    private readonly Observations observations;
    private readonly RunHistory history;
    private readonly IPluginLog log;

    public FarmController(
        IFramework framework,
        NavmeshIpc navmesh,
        WrathIpc wrath,
        IClientState clientState,
        IObjectTable objects,
        ITargetManager targets,
        IDataManager data,
        ICondition condition,
        Notifier notifier,
        Requirements requirements,
        Func<uint, string> itemName,
        Configuration config,
        Func<RunRecorder?> newRecorder,
        Observations observations,
        RunHistory history,
        IPluginLog log)
    {
        this.framework = framework;
        this.navmesh = navmesh;
        this.wrath = wrath;
        this.clientState = clientState;
        this.objects = objects;
        this.targets = targets;
        this.data = data;
        this.condition = condition;
        this.notifier = notifier;
        Requirements = requirements;
        this.itemName = itemName;
        this.config = config;
        this.newRecorder = newRecorder;
        this.observations = observations;
        this.history = history;
        this.log = log;

        framework.Update += OnUpdate;
    }

    public FarmSession? Current { get; private set; }

    public bool Running => Current is { Phase: not FarmPhase.Finished };

    public Requirements Requirements { get; }

    public string? Blocker => Requirements.Blocker;

    public void Start(FarmTarget target, StopConditions conditions)
    {
        Stop("replaced");
        Current = new FarmSession(
            target, conditions, navmesh, wrath, clientState, objects, targets, data, condition, notifier, itemName, config, newRecorder(), observations, history, log);
        log.Information(
            $"Farming {target.Name} in {target.Area.ZoneName}, {target.Area.Spots.Count} spot(s).");
        notifier.Info($"farming {target.Name} in {target.Area.ZoneName}.");
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
