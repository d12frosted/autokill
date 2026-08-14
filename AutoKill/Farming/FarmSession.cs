using System.Numerics;
using AutoKill.Core;
using AutoKill.Data;
using AutoKill.IPC;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoKill.Farming;

public enum FarmPhase
{
    Idle,
    Teleporting,
    Travelling,
    Hunting,
    Finished,
}

/// <summary>
/// One farming run: get to the mob, keep killing it, stop when told to.
/// </summary>
/// <remarks>
/// Deliberately a state machine ticked from the framework rather than an async
/// walk through the steps. Every step can be interrupted by something outside
/// our control (a loading screen, a death, vnavmesh giving up, Wrath taking its
/// lease back), and a tick that re-reads the world each time handles that
/// without unwinding anything.
/// </remarks>
public sealed class FarmSession
{
    private const float ArrivalRange = 12f;
    private const float EngageRange = 3f;
    private const float HuntRadius = 90f;

    // Below this a mount costs more time to summon than it saves.
    private const float MountDistance = 60f;

    private static readonly TimeSpan MoveCooldown = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MountCooldown = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan TeleportCooldown = TimeSpan.FromSeconds(10);

    private readonly MobEntry mob;
    private readonly FarmLocation location;
    private readonly StopConditions conditions;
    private readonly NavmeshIpc navmesh;
    private readonly WrathIpc wrath;
    private readonly IClientState clientState;
    private readonly IObjectTable objects;
    private readonly ITargetManager targets;
    private readonly IDataManager data;
    private readonly ICondition condition;
    private readonly IPluginLog log;

    private readonly DateTime startedAt = DateTime.UtcNow;
    private readonly Dictionary<uint, int> baselineCounts = [];
    private readonly Dictionary<uint, int> gained = [];
    private readonly HashSet<ulong> engaged = [];

    private DateTime lastMove = DateTime.MinValue;
    private DateTime lastMountAction = DateTime.MinValue;
    private bool flagged;
    private DateTime lastTeleport = DateTime.MinValue;
    private Vector3? resolvedSpot;
    private int kills;

    public FarmSession(
        MobEntry mob,
        FarmLocation location,
        StopConditions conditions,
        NavmeshIpc navmesh,
        WrathIpc wrath,
        IClientState clientState,
        IObjectTable objects,
        ITargetManager targets,
        IDataManager data,
        ICondition condition,
        IPluginLog log)
    {
        this.mob = mob;
        this.location = location;
        this.conditions = conditions;
        this.navmesh = navmesh;
        this.wrath = wrath;
        this.clientState = clientState;
        this.objects = objects;
        this.targets = targets;
        this.data = data;
        this.condition = condition;
        this.log = log;

        // Anything already in the bags is not something this run produced.
        foreach (var itemId in mob.Drops)
            baselineCounts[itemId] = CountOf(itemId);

        Phase = FarmPhase.Teleporting;
        Status = "starting";
    }

    public FarmPhase Phase { get; private set; }

    public string Status { get; private set; }

    public MobEntry Mob => mob;

    public FarmLocation Location => location;

    public int Kills => kills;

    public FarmProgress Progress => new(
        kills,
        DateTime.UtcNow - startedAt,
        objects.LocalPlayer?.Level ?? 0,
        InventoryFull(),
        objects.LocalPlayer is { IsDead: true },
        gained);

    public void Tick()
    {
        if (Phase == FarmPhase.Finished)
            return;

        if (objects.LocalPlayer is not { } player)
        {
            Status = "waiting for the world to load";
            return;
        }

        RefreshGains();
        CountKills();

        var progress = Progress;
        if (conditions.ShouldStop(progress))
        {
            var met = conditions.Met(progress);
            Finish(met.Count > 0 ? string.Join(", ", met.Select(c => c.Describe(progress))) : "done");
            return;
        }

        switch (Phase)
        {
            case FarmPhase.Teleporting:
                TickTeleport(player);
                break;
            case FarmPhase.Travelling:
                TickTravel(player);
                break;
            case FarmPhase.Hunting:
                TickHunt(player);
                break;
        }
    }

    public void Finish(string reason)
    {
        if (Phase == FarmPhase.Finished)
            return;

        Phase = FarmPhase.Finished;
        Status = reason;
        navmesh.Stop();
        wrath.Stop();
        log.Information($"Farming {mob.Name} stopped: {reason}");
    }

    private void TickTeleport(IPlayerCharacter player)
    {
        if (clientState.TerritoryType == location.TerritoryTypeId)
        {
            Phase = FarmPhase.Travelling;
            return;
        }

        if (clientState.IsPvP || player.IsCasting)
        {
            Status = "waiting to cast teleport";
            return;
        }

        if (DateTime.UtcNow - lastTeleport < TeleportCooldown)
        {
            Status = $"teleporting to {location.ZoneName}";
            return;
        }

        var aetheryte = Aetherytes.AttunedIn(data, location.TerritoryTypeId);
        if (aetheryte is not { } id)
        {
            Finish($"no attuned aetheryte in {location.ZoneName}");
            return;
        }

        lastTeleport = DateTime.UtcNow;
        Status = $"teleporting to {location.ZoneName}";
        if (!Aetherytes.Teleport(id))
            log.Warning($"Teleport to aetheryte {id} was refused.");
    }

    private void TickTravel(IPlayerCharacter player)
    {
        if (clientState.TerritoryType != location.TerritoryTypeId)
        {
            Phase = FarmPhase.Teleporting;
            return;
        }

        if (!navmesh.Ready)
        {
            var progress = navmesh.BuildProgress;
            Status = progress >= 0
                ? $"waiting for the navmesh ({progress * 100:F0}%)"
                : "waiting for vnavmesh";
            return;
        }

        var spot = ResolveSpot();

        // Put the destination on the map once the spot is resolved, so where
        // the character is heading is visible rather than a mystery.
        if (!flagged)
        {
            PlayerActions.FlagDestination(data, location.TerritoryTypeId, spot);
            flagged = true;
        }

        var remaining = Vector3.Distance(player.Position, spot);
        if (remaining <= ArrivalRange)
        {
            navmesh.Stop();
            Phase = FarmPhase.Hunting;
            return;
        }

        // Mounting is worth a couple of seconds over any real distance, but
        // summoning is interrupted by movement, so stop first and wait it out.
        if (remaining > MountDistance && !PlayerActions.IsMounted(condition))
        {
            if (PlayerActions.IsMounting(condition))
            {
                Status = "mounting";
                return;
            }

            if (PlayerActions.CanMount(condition) && DateTime.UtcNow - lastMountAction >= MountCooldown)
            {
                lastMountAction = DateTime.UtcNow;
                if (navmesh.Moving)
                    navmesh.Stop();
                Status = "mounting";
                PlayerActions.Mount();
                return;
            }
        }

        // Already in the air: fly the rest of the way rather than refusing to
        // path, and let the descent to a ground level spot do the landing.
        var flying = PlayerActions.IsFlying(condition);
        Status = flying
            ? $"flying to {location.ZoneName} ({remaining:F0}y)"
            : $"travelling to {location.ZoneName} ({remaining:F0}y)";

        if (navmesh.Moving || navmesh.PathfindInProgress || DateTime.UtcNow - lastMove < MoveCooldown)
            return;

        lastMove = DateTime.UtcNow;
        if (!navmesh.MoveCloseTo(spot, ArrivalRange / 2f, flying))
            log.Warning("vnavmesh would not path to the farm spot.");
    }

    private void TickHunt(IPlayerCharacter player)
    {
        if (clientState.TerritoryType != location.TerritoryTypeId)
        {
            Phase = FarmPhase.Teleporting;
            return;
        }

        var spotForLanding = ResolveSpot();

        // Dismounting in the air means falling out of the sky, possibly into
        // somewhere with no path back. Fly down to the spot first and dismount
        // once there is ground underfoot.
        if (PlayerActions.IsFlying(condition))
        {
            Status = "landing";
            if (!navmesh.Moving
                && !navmesh.PathfindInProgress
                && DateTime.UtcNow - lastMove >= MoveCooldown)
            {
                lastMove = DateTime.UtcNow;
                navmesh.MoveCloseTo(spotForLanding, ArrivalRange / 2f, true);
            }

            return;
        }

        // Nothing can be cast from the saddle, so the mount that made the
        // journey quick has to go before anything can be killed.
        if (PlayerActions.IsMounted(condition))
        {
            Status = "dismounting";
            if (navmesh.Moving)
                navmesh.Stop();
            if (DateTime.UtcNow - lastMountAction >= MountCooldown)
            {
                lastMountAction = DateTime.UtcNow;
                PlayerActions.Dismount();
            }

            return;
        }

        if (PlayerActions.IsMounting(condition))
        {
            Status = "dismounting";
            return;
        }

        if (!wrath.Leased && !wrath.Start())
            Status = "no rotation backend, fighting is up to you";

        var spot = ResolveSpot();
        var quarry = NearestQuarry(player, spot);

        if (quarry is null)
        {
            // Nothing up. Drift back to the middle of the spot so respawns land
            // around us rather than behind wherever the last pull ended.
            Status = "waiting for respawns";
            if (Vector3.Distance(player.Position, spot) > ArrivalRange
                && !navmesh.Moving
                && DateTime.UtcNow - lastMove >= MoveCooldown)
            {
                lastMove = DateTime.UtcNow;
                navmesh.MoveCloseTo(spot, ArrivalRange / 2f);
            }

            return;
        }

        engaged.Add(quarry.GameObjectId);
        if (targets.Target?.GameObjectId != quarry.GameObjectId)
            targets.Target = quarry;

        var distance = Vector3.Distance(player.Position, quarry.Position);
        Status = $"killing {mob.Name} ({kills} down)";

        if (distance <= EngageRange)
        {
            if (navmesh.Moving)
                navmesh.Stop();
            return;
        }

        if (navmesh.Moving || navmesh.PathfindInProgress || DateTime.UtcNow - lastMove < MoveCooldown)
            return;

        lastMove = DateTime.UtcNow;
        navmesh.MoveCloseTo(quarry.Position, EngageRange);
    }

    /// <summary>
    /// The recorded spot dropped onto the ground. Published data carries no
    /// usable height, so pathing to it raw either fails or aims at the sky.
    /// </summary>
    private Vector3 ResolveSpot()
    {
        if (resolvedSpot is { } cached)
            return cached;

        var floor = navmesh.PointOnFloor(location.Position, 20f);
        resolvedSpot = floor ?? location.Position;
        return resolvedSpot.Value;
    }

    private IBattleNpc? NearestQuarry(IPlayerCharacter player, Vector3 spot)
    {
        IBattleNpc? best = null;
        var bestDistance = float.MaxValue;

        foreach (var obj in objects)
        {
            if (obj is not IBattleNpc npc)
                continue;
            if (npc.BattleNpcKind != BattleNpcSubKind.Combatant)
                continue;
            if (npc.NameId != mob.BNpcNameId)
                continue;
            if (npc.IsDead || npc.CurrentHp == 0)
                continue;
            // Someone else's fight is not ours to steal.
            if (npc.TargetObject is not null && npc.TargetObjectId != player.GameObjectId)
                continue;
            if (Vector3.Distance(npc.Position, spot) > HuntRadius)
                continue;

            var distance = Vector3.Distance(npc.Position, player.Position);
            if (distance >= bestDistance)
                continue;

            best = npc;
            bestDistance = distance;
        }

        return best;
    }

    private void CountKills()
    {
        if (engaged.Count == 0)
            return;

        var dead = new List<ulong>();
        foreach (var id in engaged)
        {
            var obj = objects.SearchByEntityId((uint)id);
            if (obj is IBattleNpc { IsDead: false, CurrentHp: > 0 })
                continue;

            dead.Add(id);
        }

        foreach (var id in dead)
        {
            engaged.Remove(id);
            kills++;
        }
    }

    private void RefreshGains()
    {
        foreach (var (itemId, baseline) in baselineCounts)
        {
            var delta = CountOf(itemId) - baseline;
            if (delta > 0)
                gained[itemId] = delta;
        }
    }

    private static unsafe int CountOf(uint itemId)
    {
        var manager = InventoryManager.Instance();
        return manager == null ? 0 : manager->GetInventoryItemCount(itemId);
    }

    private static unsafe bool InventoryFull()
    {
        var manager = InventoryManager.Instance();
        if (manager == null)
            return false;

        // The four ordinary bags. Anything else is not somewhere loot lands.
        for (var type = InventoryType.Inventory1; type <= InventoryType.Inventory4; type++)
        {
            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded)
                continue;

            for (var slot = 0; slot < container->Size; slot++)
            {
                if (container->GetInventorySlot(slot)->ItemId == 0)
                    return false;
            }
        }

        return true;
    }
}
