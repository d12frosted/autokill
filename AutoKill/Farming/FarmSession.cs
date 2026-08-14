using System.Numerics;
using AutoKill.Core;
using AutoKill.Data;
using AutoKill.IPC;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Text.SeStringHandling;
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

    // Close enough to be worth stopping for on the way past.
    private const float DivertRadius = 35f;

    // How far a fight may wander from the spot before it stops counting.
    private const float LeashRadius = 45f;

    private static readonly TimeSpan MoveCooldown = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MountCooldown = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan JumpCooldown = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan TeleportCooldown = TimeSpan.FromSeconds(10);

    private readonly MobEntry mob;
    private readonly FarmArea area;
    private readonly StopConditions conditions;
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
    private readonly RunRecorder? recorder;
    private readonly IPluginLog log;

    private readonly DateTime startedAt = DateTime.UtcNow;
    private readonly Dictionary<uint, int> baselineCounts = [];
    private readonly Dictionary<uint, int> gained = [];
    private readonly HashSet<ulong> engaged = [];

    private DateTime lastMove = DateTime.MinValue;
    private DateTime lastMountAction = DateTime.MinValue;
    private DateTime lastJump = DateTime.MinValue;
    private bool flagged;
    private DateTime lastTeleport = DateTime.MinValue;
    private Vector3? resolvedSpot;
    private int spotIndex;
    private DateTime? emptySince;
    private DateTime lastSample = DateTime.MinValue;
    private int kills;

    public FarmSession(
        MobEntry mob,
        FarmArea area,
        StopConditions conditions,
        NavmeshIpc navmesh,
        WrathIpc wrath,
        IClientState clientState,
        IObjectTable objects,
        ITargetManager targets,
        IDataManager data,
        ICondition condition,
        Notifier notifier,
        Func<uint, string> itemName,
        Configuration config,
        RunRecorder? recorder,
        IPluginLog log)
    {
        this.mob = mob;
        this.area = area;
        this.conditions = conditions;
        this.navmesh = navmesh;
        this.wrath = wrath;
        this.clientState = clientState;
        this.objects = objects;
        this.targets = targets;
        this.data = data;
        this.condition = condition;
        this.notifier = notifier;
        this.itemName = itemName;
        this.config = config;
        this.recorder = recorder;
        this.log = log;

        // Anything already in the bags is not something this run produced.
        foreach (var itemId in mob.Drops)
            baselineCounts[itemId] = CountOf(itemId);

        Phase = FarmPhase.Teleporting;
        Status = "starting";

        recorder?.Write("start", new
        {
            mob = mob.Name,
            mobId = mob.BNpcNameId,
            baseIds = mob.BaseIds,
            zone = area.ZoneName,
            territory = area.TerritoryTypeId,
            spots = area.Spots.Select(spot => new
            {
                x = spot.Position.X,
                z = spot.Position.Z,
                mapX = spot.MapPosition.X,
                mapY = spot.MapPosition.Y,
                spawns = spot.SpawnCount,
            }),
            settings = new
            {
                config.MountDistance,
                config.RespawnPatienceSeconds,
                arrivalRange = ArrivalRange,
                engageRange = EngageRange,
                huntRadius = HuntRadius,
                divertRadius = DivertRadius,
                leashRadius = LeashRadius,
            },
            targets = conditions.Conditions.Select(c => c.GetType().Name),
        });
    }

    public FarmPhase Phase { get; private set; }

    public string Status { get; private set; }

    public MobEntry Mob => mob;

    public FarmArea Area => area;

    /// <summary>What the run is aiming at, so progress can be shown against it.</summary>
    public StopConditions Conditions => conditions;

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
        Record(player);

        var progress = Progress;
        if (conditions.ShouldStop(progress))
        {
            var met = conditions.Met(progress);
            Finish(
                met.Count > 0 ? string.Join(", ", met.Select(c => Describe(c, progress))) : "done",
                met,
                progress);
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

    public void Finish(string reason) => Finish(reason, [], Progress);

    /// <summary>
    /// A periodic snapshot, plus where every matching mob was standing. The
    /// second half is the point: it is what shows a target within reach being
    /// walked past in favour of a spot on a map.
    /// </summary>
    private void Record(IPlayerCharacter player)
    {
        if (recorder is null || DateTime.UtcNow - lastSample < SampleInterval)
            return;

        lastSample = DateTime.UtcNow;
        var spot = resolvedSpot;

        recorder.Write("sample", new
        {
            phase = Phase.ToString(),
            status = Status,
            spot = spotIndex % Math.Max(1, area.Spots.Count),
            x = Math.Round(player.Position.X, 1),
            z = Math.Round(player.Position.Z, 1),
            y = Math.Round(player.Position.Y, 1),
            toSpot = spot is { } s ? Math.Round(Vector3.Distance(player.Position, s), 1) : (double?)null,
            mounted = PlayerActions.IsMounted(condition),
            flying = PlayerActions.IsFlying(condition),
            inCombat = condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat],
            moving = navmesh.Moving,
            kills,
            targetId = targets.Target?.GameObjectId,
            nearby = objects
                .OfType<IBattleNpc>()
                .Where(npc => npc.NameId == mob.BNpcNameId && !npc.IsDead && npc.CurrentHp > 0)
                .Select(npc => new
                {
                    id = npc.GameObjectId,
                    d = Math.Round(Vector3.Distance(npc.Position, player.Position), 1),
                    x = Math.Round(npc.Position.X, 1),
                    z = Math.Round(npc.Position.Z, 1),
                    engaged = npc.TargetObject is not null,
                })
                .OrderBy(n => n.d)
                .Take(12),
        });
    }

    public void Finish(string reason, IReadOnlyList<IStopCondition> met, FarmProgress progress)
    {
        if (Phase == FarmPhase.Finished)
            return;

        Phase = FarmPhase.Finished;
        Status = reason;
        recorder?.Write("finish", new { reason, kills, elapsed = progress.Elapsed.TotalSeconds });
        recorder?.Dispose();
        navmesh.StopCompletely();
        wrath.Stop();
        log.Information($"Farming {mob.Name} stopped: {reason}");

        Announce(reason, met, progress);
    }

    /// <summary>
    /// Say what happened, with the items as chat links rather than numbers, so
    /// they can be hovered like any other item in the log.
    /// </summary>
    private void Announce(string reason, IReadOnlyList<IStopCondition> met, FarmProgress progress)
    {
        var chat = new SeStringBuilder().AddText($"{mob.Name}: ");
        var plain = $"{mob.Name}: ";

        // Whatever ended the run, said once. Items in it are linked where they
        // stand, so there is no need to repeat them in a tally afterwards.
        var named = new HashSet<uint>();
        if (met.Count == 0)
        {
            chat.AddText(reason);
            plain += reason;
        }
        else
        {
            for (var i = 0; i < met.Count; i++)
            {
                if (i > 0)
                {
                    chat.AddText(", ");
                    plain += ", ";
                }

                if (met[i] is ItemCountCondition item)
                {
                    named.Add(item.ItemId);
                    chat.AddItemLink(item.ItemId, false, itemName(item.ItemId));
                    chat.AddText($" {progress.CountOf(item.ItemId)}/{item.Target}");
                    plain += $"{itemName(item.ItemId)} {progress.CountOf(item.ItemId)}/{item.Target}";
                }
                else
                {
                    chat.AddText(met[i].Describe(progress));
                    plain += met[i].Describe(progress);
                }
            }
        }

        var tail = $". {kills} killed in {progress.Elapsed:hh\\:mm\\:ss}";
        chat.AddText(tail);
        plain += tail;

        // Anything else that dropped along the way, which the reason had no
        // cause to mention.
        foreach (var (itemId, count) in gained.Where(g => !named.Contains(g.Key)))
        {
            chat.AddText($", {count} ");
            chat.AddItemLink(itemId, false, itemName(itemId));
            plain += $", {count} {itemName(itemId)}";
        }

        notifier.Alert(chat.Build(), plain);
    }

    /// <summary>
    /// A condition in words. Item conditions get the item's name, since the
    /// condition itself only knows the id and an id tells a reader nothing.
    /// </summary>
    private string Describe(IStopCondition stop, FarmProgress progress) =>
        stop is ItemCountCondition item
            ? $"{itemName(item.ItemId)} {progress.CountOf(item.ItemId)}/{item.Target}"
            : stop.Describe(progress);

    private void TickTeleport(IPlayerCharacter player)
    {
        if (clientState.TerritoryType == area.TerritoryTypeId)
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
            Status = $"teleporting to {area.ZoneName}";
            return;
        }

        var aetheryte = Aetherytes.AttunedIn(data, area.TerritoryTypeId);
        if (aetheryte is not { } id)
        {
            Finish($"no attuned aetheryte in {area.ZoneName}");
            return;
        }

        lastTeleport = DateTime.UtcNow;
        Status = $"teleporting to {area.ZoneName}";
        if (!Aetherytes.Teleport(id))
            log.Warning($"Teleport to aetheryte {id} was refused.");
    }

    private void TickTravel(IPlayerCharacter player)
    {
        if (clientState.TerritoryType != area.TerritoryTypeId)
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
            PlayerActions.FlagDestination(data, area.TerritoryTypeId, spot);
            flagged = true;
        }

        // Altitude is not distance left to travel when the way down is a
        // dismount, so arriving from the air is judged on the ground plane and
        // the landing is left to the hunt.
        var remaining = PlayerActions.IsFlying(condition)
            ? Horizontally(player.Position, spot)
            : Vector3.Distance(player.Position, spot);

        if (remaining <= ArrivalRange)
        {
            navmesh.Stop();
            Phase = FarmPhase.Hunting;
            return;
        }

        // Mounting is worth a couple of seconds over any real distance, but
        // summoning is interrupted by movement, so stop first and wait it out.
        if (remaining > config.MountDistance && !PlayerActions.IsMounted(condition))
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
                recorder?.Write("mount", new { remaining = Math.Round(remaining, 1) });
                PlayerActions.Mount();
                return;
            }
        }

        // Fly when the zone allows it, which is how anyone actually covers this
        // sort of distance.
        var flying = PlayerActions.IsFlying(condition);
        if (!flying
            && remaining > config.MountDistance
            && PlayerActions.IsMounted(condition)
            && PlayerActions.CanFlyIn(data, area.TerritoryTypeId))
        {
            // Take off deliberately rather than leaving it to the path. vnavmesh
            // only jumps once a path climbs, and a path between two points on
            // the ground has no reason to, so waiting for that means running the
            // whole way with a flying mount underneath.
            Status = "taking off";
            if (DateTime.UtcNow - lastJump >= JumpCooldown)
            {
                lastJump = DateTime.UtcNow;
                recorder?.Write("takeoff", new { remaining = Math.Round(remaining, 1) });
                PlayerActions.Jump();
            }

            return;
        }

        // Walking through something we came here to kill and then walking back
        // to it is daft. Flying past is left alone: landing early costs more
        // than the detour saves.
        if (!flying && FindQuarry(player, spot, DivertRadius, 0f) is { } passing)
        {
            recorder?.Write("divert", new
            {
                distance = Math.Round(Vector3.Distance(passing.Position, player.Position), 1),
                stillToSpot = Math.Round(remaining, 1),
            });
            navmesh.Stop();
            Phase = FarmPhase.Hunting;
            return;
        }

        Status = flying
            ? $"flying to {area.ZoneName} ({remaining:F0}y)"
            : $"travelling to {area.ZoneName} ({remaining:F0}y)";

        if (navmesh.Moving || navmesh.PathfindInProgress || DateTime.UtcNow - lastMove < MoveCooldown)
            return;

        lastMove = DateTime.UtcNow;
        if (!navmesh.MoveCloseTo(spot, ArrivalRange / 2f, flying))
            log.Warning("vnavmesh would not path to the farm spot.");
    }

    private void TickHunt(IPlayerCharacter player)
    {
        if (clientState.TerritoryType != area.TerritoryTypeId)
        {
            Phase = FarmPhase.Teleporting;
            return;
        }

        // Coming in on a flying mount. Get over the spot first, then dismount,
        // which is what actually puts the character on the ground: pathing
        // towards a floor point only ever hovers above it.
        if (PlayerActions.IsFlying(condition))
        {
            var spotForLanding = ResolveSpot();
            var overhead = Horizontally(player.Position, spotForLanding);

            if (overhead > ArrivalRange)
            {
                Status = $"flying in ({overhead:F0}y)";
                if (!navmesh.Moving
                    && !navmesh.PathfindInProgress
                    && DateTime.UtcNow - lastMove >= MoveCooldown)
                {
                    lastMove = DateTime.UtcNow;
                    navmesh.MoveCloseTo(spotForLanding, ArrivalRange / 2f, true);
                }

                return;
            }

            Status = "landing";
            if (navmesh.Moving)
                navmesh.Stop();
            if (DateTime.UtcNow - lastMountAction >= MountCooldown)
            {
                lastMountAction = DateTime.UtcNow;
                // Recorded with what was actually standing about, because
                // dismounting for an empty field is exactly the waste worth
                // catching.
                recorder?.Write("dismount", new { quarryNearby = FindQuarry(player, ResolveSpot(), HuntRadius, HuntRadius) is not null });
                PlayerActions.Dismount();
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
                // Recorded with what was actually standing about, because
                // dismounting for an empty field is exactly the waste worth
                // catching.
                recorder?.Write("dismount", new { quarryNearby = FindQuarry(player, ResolveSpot(), HuntRadius, HuntRadius) is not null });
                PlayerActions.Dismount();
            }

            return;
        }

        if (PlayerActions.IsMounting(condition))
        {
            Status = "dismounting";
            return;
        }

        if (!wrath.Rotating && !wrath.Start())
            Status = "no rotation backend, fighting is up to you";

        var spot = ResolveSpot();
        var quarry = FindQuarry(player, spot, LeashRadius, HuntRadius);

        if (quarry is null)
        {
            emptySince ??= DateTime.UtcNow;

            // Cleared. Standing about waiting for a respawn timer wastes the
            // rest of the area, so move on round the circuit instead; by the
            // time it comes back here this knot has repopulated.
            if (DateTime.UtcNow - emptySince >= TimeSpan.FromSeconds(config.RespawnPatienceSeconds))
            {
                // Diverting to something on the way leaves the spot unreached,
                // and the circuit should not skip it just because the detour
                // came up empty.
                if (Vector3.Distance(player.Position, spot) > ArrivalRange)
                {
                    emptySince = null;
                    Phase = FarmPhase.Travelling;
                    return;
                }

                AdvanceSpot();
                return;
            }

            Status = area.Spots.Count > 1 ? "cleared, moving on" : "waiting for respawns";
            if (Vector3.Distance(player.Position, spot) > ArrivalRange
                && !navmesh.Moving
                && DateTime.UtcNow - lastMove >= MoveCooldown)
            {
                lastMove = DateTime.UtcNow;
                navmesh.MoveCloseTo(spot, ArrivalRange / 2f);
            }

            return;
        }

        emptySince = null;

        engaged.Add(quarry.GameObjectId);
        if (targets.Target?.GameObjectId != quarry.GameObjectId)
            targets.Target = quarry;

        var distance = Vector3.Distance(player.Position, quarry.Position);
        Status = area.Spots.Count > 1
            ? $"killing {mob.Name}, spot {spotIndex % area.Spots.Count + 1} of {area.Spots.Count}"
            : $"killing {mob.Name}";

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
    private FarmLocation CurrentSpot => area.Spots[spotIndex % area.Spots.Count];

    /// <summary>
    /// The current spot, dropped onto the ground.
    /// </summary>
    /// <remarks>
    /// Spawn data carries no height, so the search starts from high above and
    /// falls, the same trick vnavmesh uses to turn a map flag into a point.
    /// Starting from a height of zero asks about the middle of the world, which
    /// in most zones is solid rock or empty sky.
    ///
    /// A failed query is not cached. It usually means the navmesh is still
    /// loading, and caching the fallback would strand the run at a point it can
    /// never reach for as long as it lasts.
    /// </remarks>
    private Vector3 ResolveSpot()
    {
        if (resolvedSpot is { } cached)
            return cached;

        var spot = CurrentSpot.Position;
        if (navmesh.PointOnFloor(spot with { Y = 1024f }, 20f) is { } floor)
        {
            resolvedSpot = floor;
            return floor;
        }

        // Somewhere plausible until the mesh can answer: the character's own
        // height is at least a height they can stand at.
        return spot with { Y = objects.LocalPlayer?.Position.Y ?? 0f };
    }

    /// <summary>
    /// Move on to the next knot of the circuit. Going back through travelling
    /// rather than walking there from the hunt is deliberate: travelling already
    /// knows how to mount, fly and flag the map, and the next spot is exactly
    /// the kind of distance that is worth doing all three for.
    /// </summary>
    private void AdvanceSpot()
    {
        if (area.Spots.Count <= 1)
        {
            emptySince = null;
            return;
        }

        recorder?.Write("advance", new { from = spotIndex % area.Spots.Count });
        spotIndex = (spotIndex + 1) % area.Spots.Count;
        resolvedSpot = null;
        flagged = false;
        emptySince = null;
        navmesh.Stop();
        Phase = FarmPhase.Travelling;
    }

    /// <summary>
    /// Distance ignoring height. Altitude says nothing about whether the right
    /// place has been reached when the way down is a dismount.
    /// </summary>
    private static float Horizontally(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    /// <summary>
    /// The nearest worthwhile target, counting anything near the spot as well as
    /// anything near the character. The second radius is what makes it possible
    /// to stop for something on the way rather than walking past it.
    /// </summary>
    private IBattleNpc? FindQuarry(
        IPlayerCharacter player, Vector3 spot, float playerRadius, float spotRadius)
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
            var fromSpot = Vector3.Distance(npc.Position, spot);
            var fromPlayer = Vector3.Distance(npc.Position, player.Position);
            if (fromSpot > spotRadius && fromPlayer > playerRadius)
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
