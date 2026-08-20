using AutoKill.Core;
using AutoKill.Data;
using AutoKill.IPC;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace AutoKill.Farming;

/// <summary>Owns the running session and ticks it.</summary>
public sealed class FarmController : IDisposable
{
    // Long enough to wait out the combat a last kill leaves trailing; short
    // enough that giving up does not surprise anyone half a minute later.
    private static readonly TimeSpan HomePatience = TimeSpan.FromSeconds(30);

    // Longer than a teleport cast takes to leave, so an accepted cast is not
    // cancelled by the next ask.
    private static readonly TimeSpan HomeRetry = TimeSpan.FromSeconds(6);

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

    // Where the last run set off from, and the trip back there when one is
    // wanted and going.
    private uint homeTerritory;
    private Homecoming? homecoming;

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

        homecoming = null;
        Stop("replaced");
        homeTerritory = clientState.TerritoryType;
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
        TickHome();

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

        // Only a finish that happened inside the tick starts the trip back.
        // The Stop button finishes a session between ticks, and whoever pressed
        // it is standing right there: teleporting them away would be one more
        // way of fighting the player for the character.
        if (session.Phase == FarmPhase.Finished)
            ConsiderHome();
    }

    private void ConsiderHome()
    {
        if (!config.ReturnWhenDone || homecoming is not null)
            return;

        // Already there, so there is no trip. Dying is excluded too: the game
        // is about to offer its own way back, and racing it is not a favour.
        if (clientState.TerritoryType == homeTerritory)
            return;
        if (objects.LocalPlayer is null or { IsDead: true })
            return;

        if (Aetherytes.AttunedIn(data, homeTerritory) is null)
        {
            notifier.Info("no attuned aetheryte to teleport back to, staying put.");
            return;
        }

        homecoming = new Homecoming(HomePatience, HomeRetry);
        notifier.Info("teleporting back once the dust settles.");
    }

    private void TickHome()
    {
        if (homecoming is not { } trip)
            return;

        if (clientState.TerritoryType == homeTerritory)
        {
            homecoming = null;
            return;
        }

        var player = objects.LocalPlayer;
        var busy = player is null or { IsDead: true } || player.IsCasting
            || condition[ConditionFlag.InCombat]
            || condition[ConditionFlag.BetweenAreas]
            || condition[ConditionFlag.BetweenAreas51];

        switch (trip.Check(busy, DateTime.UtcNow))
        {
            case HomeStep.GiveUp:
                homecoming = null;
                notifier.Info("could not teleport back, staying put.");
                break;

            case HomeStep.Go:
                if (Aetherytes.AttunedIn(data, homeTerritory) is { } id)
                    Aetherytes.Teleport(id);
                else
                    homecoming = null;
                break;
        }
    }
}
