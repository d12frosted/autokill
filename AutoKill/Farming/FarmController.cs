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
    private readonly Jobs jobs;
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
        Jobs jobs,
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
        this.jobs = jobs;
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

    public Jobs Jobs => jobs;

    /// <summary>
    /// Go after this, unless the character is in no state to. False means
    /// nothing started and the reason has already been said out loud.
    /// </summary>
    /// <remarks>
    /// The window checks the same thing while the target is on screen and greys
    /// the button out, so getting here with a bad job means something changed
    /// between the last frame and the press. Checked again anyway, since the one
    /// place it must not be wrong is the moment it acts.
    /// </remarks>
    public bool Start(FarmTarget target, StopConditions conditions)
    {
        var job = jobs.Plan(target.Area.Level);
        if (job.Says is { } says)
            notifier.Info(says);

        if (job.Blocked)
        {
            log.Information($"Not farming {target.Name}: {job.Says}");
            return false;
        }

        if (job.Change is { } change && !jobs.Equip(change))
        {
            notifier.Info($"could not put {change.GearsetName} on, so nothing started.");
            return false;
        }

        Stop("replaced");
        Current = new FarmSession(
            target, conditions, navmesh, wrath, clientState, objects, targets, data, condition, notifier, itemName, config, newRecorder(), observations, history, log);
        log.Information(
            $"Farming {target.Name} in {target.Area.ZoneName}, {target.Area.Spots.Count} spot(s).");
        notifier.Info($"farming {target.Name} in {target.Area.ZoneName}.");
        return true;
    }

    public void Stop(string reason = "stopped")
    {
        Current?.Finish(reason);
    }

    public void Pause() => Current?.Pause("waiting on you");

    public void Resume() => Current?.Resume();

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
