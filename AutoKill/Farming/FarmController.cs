using AutoKill.Core;
using AutoKill.Data;
using AutoKill.IPC;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace AutoKill.Farming;

/// <summary>
/// One stop of a run over a crafting list: a field, the material it is for,
/// and how much of it the list wants in total. How much is still missing is
/// worked out when the leg starts, not when it is queued, because the stops
/// before it change what the bags hold.
/// </summary>
public readonly record struct FarmLeg(FarmTarget Target, uint ItemId, int Required);

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

    // Where the last run set off from, and the trip back when one is wanted
    // and going. The destination is resolved once when the trip starts and
    // held, not re-asked every tick.
    private uint homeTerritory;
    private Homecoming? homecoming;
    private (uint Territory, uint Aetheryte) returnTo;

    // The stops still ahead of the current session, when a whole crafting list
    // is being farmed rather than one thing.
    private readonly Queue<FarmLeg> legs = new();

    /// <summary>How many stops are queued after the running one.</summary>
    public int Queued => legs.Count;

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
        legs.Clear();
        homecoming = null;
        homeTerritory = clientState.TerritoryType;
        return StartCore(target, conditions);
    }

    /// <summary>
    /// Farm a whole list of stops, one after another. Each leg's goal is
    /// worked out when it starts, from what the bags hold by then, so a stop
    /// that turns out to be covered is skipped rather than farmed for nothing.
    /// </summary>
    public void StartMany(IEnumerable<FarmLeg> list)
    {
        legs.Clear();
        foreach (var leg in list)
            legs.Enqueue(leg);

        homecoming = null;
        homeTerritory = clientState.TerritoryType;
        StartNextLeg();
    }

    private bool StartCore(FarmTarget target, StopConditions conditions)
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

        Halt("replaced");
        Current = new FarmSession(
            target, conditions, navmesh, wrath, clientState, objects, targets, data, condition, notifier, itemName, config, newRecorder(), observations, history, log);
        log.Information(
            $"Farming {target.Name} in {target.Area.ZoneName}, {target.Area.Spots.Count} spot(s).");
        notifier.Info($"farming {target.Name} in {target.Area.ZoneName}.");
        return true;
    }

    private void StartNextLeg()
    {
        while (legs.TryDequeue(out var leg))
        {
            var missing = CraftingLists.StillNeeded(leg.Required, Bags.CountOf(leg.ItemId));
            if (missing == 0)
                continue;

            var conditions = new StopConditions(
                [
                    new ItemCountCondition(leg.ItemId, missing),
                    new DeathCondition(),
                    new InventoryFullCondition(),
                ],
                StopMode.Any);

            // A stop the job checks refuse has said why already; the stops
            // after it are still worth trying.
            if (!StartCore(leg.Target, conditions))
                continue;

            if (legs.Count > 0)
                notifier.Info($"{legs.Count} more stop(s) on the list after this one.");
            return;
        }
    }

    public void Stop(string reason = "stopped")
    {
        // Stopping by hand stops the whole list; leaving the rest queued would
        // relaunch it the moment this finish was noticed.
        legs.Clear();
        Halt(reason);
    }

    /// <summary>End the running session without touching the queue.</summary>
    private void Halt(string reason) => Current?.Finish(reason);

    public void Pause() => Current?.Pause("waiting on you");

    public void Resume() => Current?.Resume();

    /// <summary>Flag an area on the game map and open it there.</summary>
    public void ShowOnMap(FarmArea area) =>
        PlayerActions.ShowOnMap(data, area.TerritoryTypeId, area.Centre);

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

        // Only a finish that happened inside the tick moves on to the next
        // stop or starts the trip back. The Stop button finishes a session
        // between ticks and clears the queue itself, and whoever pressed it is
        // standing right there: teleporting them away would be one more way of
        // fighting the player for the character.
        if (session.Phase != FarmPhase.Finished)
            return;

        if (legs.Count > 0)
        {
            // Only a leg that got what it came for hands over to the next.
            // Dying, full bags or an unreachable field would fail every stop
            // after it the same way, and marching on through them says nothing
            // a second failure has not already said.
            if (LegDone(session))
            {
                StartNextLeg();
                if (Running)
                    return;
            }
            else
            {
                legs.Clear();
                notifier.Info("the rest of the list stops here too.");
            }
        }

        ConsiderHome();
    }

    private static bool LegDone(FarmSession session) =>
        session.Outcome is { } outcome
        && session.Conditions.Conditions.OfType<ItemCountCondition>().Any(c => c.IsMet(outcome));

    /// <summary>
    /// Where back is: an aetheryte in the zone the run set off from, or the
    /// home point, as configured. Nothing when there is no way there.
    /// </summary>
    private (uint Territory, uint Aetheryte)? ReturnTo()
    {
        if (config.ReturnDestination == ReturnDestination.Home)
        {
            return Aetherytes.Home(data) is { } home
                ? (home.TerritoryTypeId, home.AetheryteId)
                : null;
        }

        return Aetherytes.AttunedIn(data, homeTerritory) is { } id
            ? (homeTerritory, id)
            : null;
    }

    private void ConsiderHome()
    {
        if (!config.ReturnWhenDone || homecoming is not null)
            return;

        // Dying is excluded: the game is about to offer its own way back, and
        // racing it is not a favour.
        if (objects.LocalPlayer is null or { IsDead: true })
            return;

        if (ReturnTo() is not { } back)
        {
            notifier.Info(config.ReturnDestination == ReturnDestination.Home
                ? "no home point set, staying put."
                : "no attuned aetheryte to teleport back to, staying put.");
            return;
        }

        // Already there, so there is no trip.
        if (clientState.TerritoryType == back.Territory)
            return;

        returnTo = back;
        homecoming = new Homecoming(HomePatience, HomeRetry);
        notifier.Info("teleporting back once the dust settles.");
    }

    private void TickHome()
    {
        if (homecoming is not { } trip)
            return;

        if (clientState.TerritoryType == returnTo.Territory)
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
                Aetherytes.Teleport(returnTo.Aetheryte);
                break;
        }
    }
}
