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
    private const float MeleeRange = 3f;

    // Inside the twenty five yalm cap every ranged attack shares, with room for
    // the target to shuffle without pulling the character in after it.
    private const float RangedRange = 20f;
    private const float HuntRadius = 90f;

    // Anything of interest this far from the character is worth going to
    // instead of a point on a map. A spot is only ever a guess at where mobs
    // will be; a mob that can be seen is not a guess.
    private const float VisionRadius = 120f;

    // How far a fight may wander from the spot before it stops counting.
    private const float LeashRadius = 45f;

    private static readonly TimeSpan MoveCooldown = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MountCooldown = TimeSpan.FromSeconds(3);

    // Dismounting is cheap to ask for again and the descent takes a moment, so
    // waiting a whole mount cooldown between attempts is most of the time spent
    // getting out of the saddle.
    private static readonly TimeSpan DismountRetry = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SightingInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CompanionInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RejectionInterval = TimeSpan.FromSeconds(2);
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
    private readonly Observations observations;
    private readonly RunHistory history;
    private readonly IPluginLog log;

    private readonly DateTime startedAt = DateTime.UtcNow;
    private readonly Dictionary<uint, int> baselineCounts = [];
    private readonly Dictionary<uint, int> gained = [];
    private readonly HashSet<ulong> engaged = [];
    private readonly Dictionary<ulong, DateTime> engagedAt = [];

    private DateTime lastMove = DateTime.MinValue;
    private DateTime lastMountAction = DateTime.MinValue;
    private bool flagged;
    private DateTime lastTeleport = DateTime.MinValue;
    private Vector3? resolvedSpot;
    private int spotIndex;
    private DateTime? emptySince;
    private ulong chosen;
    private Vector3 lastPosition;
    private DateTime? stuckSince;
    private bool stuckReported;
    private DateTime lastCompanionCheck = DateTime.MinValue;
    private bool companionWarned;
    private bool lastFly;
    private DateTime lastRejection = DateTime.MinValue;
    private DateTime lastSample = DateTime.MinValue;
    private DateTime lastSighting = DateTime.MinValue;
    private readonly Dictionary<int, DateTime> clearedAt = [];
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
        Observations observations,
        RunHistory history,
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
        this.observations = observations;
        this.history = history;
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
                visionRadius = VisionRadius,
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

        if (navmesh.Moving && Vector3.Distance(player.Position, lastPosition) < 0.5f)
        {
            stuckSince ??= DateTime.UtcNow;
            if (DateTime.UtcNow - stuckSince >= StuckAfter && !stuckReported)
            {
                stuckReported = true;
                recorder?.Write("stuck", new
                {
                    phase = Phase.ToString(),
                    status = Status,
                    seconds = Math.Round((DateTime.UtcNow - stuckSince.Value).TotalSeconds, 1),
                    flying = PlayerActions.IsFlying(condition),
                });
            }
        }
        else
        {
            stuckSince = null;
            stuckReported = false;
        }

        lastPosition = player.Position;

        if (DateTime.UtcNow - lastSighting >= SightingInterval)
        {
            lastSighting = DateTime.UtcNow;
            foreach (var npc in objects.OfType<IBattleNpc>()
                         .Where(n => n.NameId == mob.BNpcNameId && !n.IsDead && n.CurrentHp > 0))
            {
                observations.RecordSighting(
                    mob.BNpcNameId, area.TerritoryTypeId, npc.Position.X, npc.Position.Z);
            }
        }

        recorder?.Write("sample", new
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
            rotating = wrath.Rotating,
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
        observations.Save();
        Remember(reason, progress);
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

    /// <summary>
    /// File the run away, with what it was asked to do as well as what it did,
    /// so repeating it asks for the same thing rather than starting blank.
    /// </summary>
    private void Remember(string reason, FarmProgress progress)
    {
        history.Add(new RunRecord
        {
            When = DateTime.Now,
            MobId = mob.BNpcNameId,
            MobName = mob.Name,
            TerritoryId = area.TerritoryTypeId,
            AreaX = area.Centre.X,
            AreaZ = area.Centre.Z,
            Kills = kills,
            ElapsedSeconds = Math.Round(progress.Elapsed.TotalSeconds),
            Reason = reason,
            Gained = new Dictionary<uint, int>(gained),
            KillTarget = conditions.Conditions.OfType<KillCountCondition>().FirstOrDefault()?.Target ?? 0,
            MinuteTarget = conditions.Conditions.OfType<ElapsedCondition>().FirstOrDefault()?.Limit.TotalMinutes ?? 0,
            ItemTargets = conditions.Conditions
                .OfType<ItemCountCondition>()
                .ToDictionary(c => c.ItemId, c => c.Target),
            RequireAll = conditions.Mode == StopMode.All,
        });
    }

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
                PlayerActions.Mount(PlayerActions.CanFlyIn(data, area.TerritoryTypeId));
                return;
            }
        }

        // Anything visible beats the spot we were heading for. A spot is a
        // guess at where mobs stand; a mob is where one is actually standing.
        if (FindQuarry(player, spot, VisionRadius, 0f) is { } visible)
        {
            recorder?.Write("divert", new
            {
                distance = Math.Round(Vector3.Distance(visible.Position, player.Position), 1),
                stillToSpot = Math.Round(remaining, 1),
            });
            navmesh.Stop();
            Phase = FarmPhase.Hunting;
            return;
        }

        // Ask for a route through the air whenever flight is possible and the
        // character is mounted. vnavmesh climbs to cross ground and jumps by
        // itself to get off it, so taking off is not ours to arrange.
        var flying = ShouldFly();

        var where = area.Spots.Count > 1
            ? $"spot {spotIndex % area.Spots.Count + 1} of {area.Spots.Count}"
            : "the spot";
        Status = $"nothing in sight, {(flying ? "flying" : "heading")} to {where}, {remaining:F0}y";

        Steer(spot, ArrivalRange / 2f);
    }

    /// <summary>
    /// How close this job needs to be. Walking a caster into melee wastes the
    /// approach and puts it somewhere it has no business standing.
    /// </summary>
    private float EngageRange
    {
        get
        {
            // ClassJob roles: 1 tank, 2 melee, 3 ranged, 4 healer.
            var role = objects.LocalPlayer?.ClassJob.ValueNullable?.Role ?? 0;
            return role is 3 or 4 ? RangedRange : MeleeRange;
        }
    }

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

        var from = spotIndex % area.Spots.Count;
        clearedAt[from] = DateTime.UtcNow;

        var expected = observations.For(mob.BNpcNameId, area.TerritoryTypeId).TypicalRepopulation()
                       ?? TimeSpan.FromSeconds(90);

        var here = objects.LocalPlayer?.Position ?? area.Centre;
        var states = area.Spots
            .Select((spot, i) => new SpotState(
                i,
                spot.SpawnCount,
                clearedAt.TryGetValue(i, out var when) ? DateTime.UtcNow - when : null,
                Vector3.Distance(here, spot.Position)))
            .ToList();

        var next = SpotRotation.PickNext(states, from, expected, jitter: 0.25);

        // Every candidate with what it was judged on, because "why did it go
        // there" is otherwise unanswerable after the fact.
        recorder?.Write("advance", new
        {
            from,
            to = next,
            expectedRespawn = Math.Round(expected.TotalSeconds, 1),
            candidates = states.Select(state => new
            {
                spot = state.Index,
                spawns = state.SpawnCount,
                away = Math.Round(state.Distance, 1),
                clearedAgo = state.SinceCleared is { } ago ? Math.Round(ago.TotalSeconds, 1) : (double?)null,
                score = Math.Round(SpotRotation.Score(state, expected), 3),
            }),
        });
        spotIndex = next;
        resolvedSpot = null;
        flagged = false;
        emptySince = null;
        navmesh.Stop();
        Phase = FarmPhase.Travelling;
    }

    private void TickHunt(IPlayerCharacter player)
    {
        if (clientState.TerritoryType != area.TerritoryTypeId)
        {
            Phase = FarmPhase.Teleporting;
            return;
        }

        var spot = ResolveSpot();
        var quarry = FindQuarry(player, spot, VisionRadius, HuntRadius);

        if (quarry is null)
        {
            RecordRejections(player);
            TickEmptySpot(player, spot);
            return;
        }

        // Finding something here again closes the loop on how long this spot
        // took to come back, which is what tells the circuit when to return.
        if (clearedAt.Remove(spotIndex % area.Spots.Count, out var clearedWhen))
        {
            var taken = DateTime.UtcNow - clearedWhen;
            observations.RecordRepopulation(mob.BNpcNameId, area.TerritoryTypeId, taken);
            recorder?.Write("repopulated", new
            {
                spot = spotIndex % area.Spots.Count,
                seconds = Math.Round(taken.TotalSeconds, 1),
            });
        }

        emptySince = null;
        if (engaged.Add(quarry.GameObjectId))
            engagedAt[quarry.GameObjectId] = DateTime.UtcNow;

        if (chosen != quarry.GameObjectId)
        {
            chosen = quarry.GameObjectId;
            recorder?.Write("target", new
            {
                id = quarry.GameObjectId,
                away = Math.Round(Vector3.Distance(quarry.Position, player.Position), 1),
                fromSpot = Math.Round(Vector3.Distance(quarry.Position, spot), 1),
                inSight = Nearby(player),
                phase = Phase.ToString(),
            });
        }

        // Measured on the ground plane while flying, since altitude is not
        // distance to something standing below.
        var distance = PlayerActions.IsFlying(condition)
            ? Horizontally(player.Position, quarry.Position)
            : Vector3.Distance(player.Position, quarry.Position);

        // Not yet within reach. Ride if it is worth riding, walk if it is not.
        //
        // The threshold is what this job can attack from, not what is worth
        // mounting for. Routing to within attack range and then judging arrival
        // by the mounting distance meant a ranged job stopped exactly where it
        // had been told to and then decided it was still too far, forever.
        if (distance > EngageRange)
        {
            Status = $"going to a {mob.Name}, {distance:F0}y away";

            // Stop a little inside reach so arriving is unambiguous rather than
            // a question of rounding.
            var closeTo = EngageRange * 0.8f;

            // What is worth mounting for is the ground still to cover, which is
            // the distance less the reach, not the distance itself. Measured the
            // other way, a ranged job that had just dismounted at the edge of
            // its reach would mount again the moment its target shuffled a yalm
            // further off, and spend the fight climbing on and off.
            if (distance - EngageRange > config.MountDistance)
                Approach(player, quarry.Position, closeTo);
            else
                Steer(quarry.Position, closeTo);

            return;
        }

        // Close enough to fight, so get out of the saddle. Nothing can be cast
        // from it, and in the air there is nothing to stand on.
        if (PlayerActions.IsFlying(condition) || PlayerActions.IsMounted(condition))
        {
            Status = "dismounting";
            if (navmesh.Moving)
                navmesh.Stop();
            if (DateTime.UtcNow - lastMountAction >= DismountRetry)
            {
                lastMountAction = DateTime.UtcNow;
                recorder?.Write("dismount", new { distance = Math.Round(distance, 1) });
                PlayerActions.Dismount(condition);
            }

            return;
        }

        if (PlayerActions.IsMounting(condition))
        {
            Status = "dismounting";
            return;
        }

        KeepCompanion();

        if (!wrath.Rotating && !wrath.Start())
            Status = "no rotation backend, fighting is up to you";

        if (targets.Target?.GameObjectId != quarry.GameObjectId)
            targets.Target = quarry;

        Status = $"killing a {mob.Name}, {Nearby(player)} in sight";

        // Within reach by the time we get here, so stand still and let the
        // rotation work rather than shuffling closer.
        if (navmesh.Moving)
            navmesh.Stop();
    }

    /// <summary>
    /// Nothing alive here. Stay in the saddle: the next thing to do is either
    /// wait or move on, and neither is helped by standing on the ground.
    /// </summary>
    private void TickEmptySpot(IPlayerCharacter player, Vector3 spot)
    {
        emptySince ??= DateTime.UtcNow;

        if (DateTime.UtcNow - emptySince >= TimeSpan.FromSeconds(config.RespawnPatienceSeconds))
        {
            // Diverting to something on the way leaves the spot unreached, and
            // the circuit should not skip it just because the detour came up
            // empty.
            if (Vector3.Distance(player.Position, spot) > ArrivalRange)
            {
                emptySince = null;
                Phase = FarmPhase.Travelling;
                return;
            }

            AdvanceSpot();
            return;
        }

        Status = area.Spots.Count > 1
            ? $"spot {spotIndex % area.Spots.Count + 1} cleared, moving on"
            : "waiting for respawns";

        if (Vector3.Distance(player.Position, spot) > ArrivalRange)
            Approach(player, spot, ArrivalRange / 2f);
    }

    /// <summary>
    /// Cover ground the way a person would: on a mount, in the air where that
    /// is allowed, and without stopping to walk the last stretch.
    /// </summary>
    private void Approach(IPlayerCharacter player, Vector3 destination, float range)
    {
        if (!PlayerActions.IsFlying(condition) && !PlayerActions.IsMounted(condition))
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
                recorder?.Write("mount", new { remaining = Math.Round(Vector3.Distance(player.Position, destination), 1) });
                PlayerActions.Mount(PlayerActions.CanFlyIn(data, area.TerritoryTypeId));
                return;
            }
        }

        Steer(destination, range);
    }

    /// <summary>
    /// Whether to ask for a route through the air.
    /// </summary>
    /// <remarks>
    /// Already flying, or mounted somewhere flight is allowed. vnavmesh routes
    /// over the ground rather than across it and jumps by itself to leave it, so
    /// arranging the takeoff here is unnecessary. An earlier version did jump
    /// deliberately, but that was written while every destination was hundreds
    /// of yalms underground, where no route would ever climb.
    /// </remarks>
    private bool ShouldFly() =>
        PlayerActions.IsFlying(condition)
        || (PlayerActions.IsMounted(condition) && PlayerActions.CanFlyIn(data, area.TerritoryTypeId));

    /// <summary>
    /// Send the character somewhere, and keep the route in step with how it is
    /// travelling.
    /// </summary>
    /// <remarks>
    /// A route carries the mode it was asked for. One begun on foot stays a
    /// ground route however the character continues, so mounting partway leaves
    /// it running along the ground with a flying mount underneath and no reason
    /// to ever leave it. vnavmesh only jumps for a route that climbs, and a
    /// ground route never does.
    ///
    /// So a change of mode re-issues the route rather than waiting for the
    /// current one to finish, which is the same thing GatherBuddy Reborn does
    /// when its mount finally arrives.
    /// </remarks>
    private void Steer(Vector3 destination, float range)
    {
        var fly = ShouldFly();
        var modeChanged = navmesh.Moving && fly != lastFly;

        if (!modeChanged
            && (navmesh.Moving || navmesh.PathfindInProgress || DateTime.UtcNow - lastMove < MoveCooldown))
            return;

        if (modeChanged)
        {
            recorder?.Write("remode", new { to = fly ? "air" : "ground" });
            navmesh.Stop();
        }

        lastMove = DateTime.UtcNow;
        lastFly = fly;
        Moved(navmesh.MoveCloseTo(destination, range, fly), destination, range);
    }

    /// <summary>Note whether vnavmesh accepted a route, since a refusal is silent otherwise.</summary>
    private void Moved(bool accepted, Vector3 destination, float range)
    {
        recorder?.Write("path", new
        {
            accepted,
            fly = ShouldFly(),
            range,
            x = Math.Round(destination.X, 1),
            z = Math.Round(destination.Z, 1),
        });

        if (!accepted)
            log.Warning("vnavmesh would not path there.");
    }

    /// <summary>
    /// Keep the chocobo out, if it is wanted.
    /// </summary>
    /// <remarks>
    /// Called from the hunt rather than once at the start, because the timer
    /// runs down over a long grind and the summon is refused in combat, so
    /// there is no single moment that works. Checked rarely: it is two reads and
    /// nothing to do almost every time.
    /// </remarks>
    private void KeepCompanion()
    {
        if (!config.SummonCompanion || DateTime.UtcNow - lastCompanionCheck < CompanionInterval)
            return;

        lastCompanionCheck = DateTime.UtcNow;

        // Well before it expires, since summoning needs a gap in the fighting.
        if (Companion.TimeLeft < 300f)
        {
            if (!Companion.HasGreens())
            {
                if (!companionWarned)
                {
                    companionWarned = true;
                    notifier.Info("out of Gysahl Greens, so the chocobo stays home.");
                }

                return;
            }

            if (Companion.Summon(condition))
                recorder?.Write("companion", new { what = "summoned" });

            return;
        }

        if (Companion.SetStance(config.CompanionStance))
            recorder?.Write("companion", new { what = "stance", to = config.CompanionStance.ToString() });
    }

    /// <summary>How many of the quarry are in sight, for saying so.</summary>
    private int Nearby(IPlayerCharacter player) =>
        objects.OfType<IBattleNpc>()
            .Count(npc => npc.NameId == mob.BNpcNameId
                          && !npc.IsDead
                          && npc.CurrentHp > 0
                          && Vector3.Distance(npc.Position, player.Position) <= VisionRadius);

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
    /// <summary>
    /// Why each candidate was passed over, recorded when nothing was picked.
    /// "It ignored one standing right there" is otherwise unanswerable, and the
    /// reason is usually one of these rather than a fault in the search.
    /// </summary>
    private void RecordRejections(IPlayerCharacter player)
    {
        // Every tick, unthrottled, this wrote two thousand records in five
        // minutes and said the same thing each time.
        if (recorder is null || DateTime.UtcNow - lastRejection < RejectionInterval)
            return;

        lastRejection = DateTime.UtcNow;

        var rejected = objects.OfType<IBattleNpc>()
            .Where(npc => npc.NameId == mob.BNpcNameId)
            .Select(npc => new
            {
                away = Math.Round(Vector3.Distance(npc.Position, player.Position), 1),
                why = npc.IsDead || npc.CurrentHp == 0 ? "dead"
                    : npc.BattleNpcKind != BattleNpcSubKind.Combatant ? "not a combatant"
                    : npc.TargetObject is not null && npc.TargetObjectId != player.GameObjectId ? "someone else's"
                    : "out of range",
            })
            .OrderBy(r => r.away)
            .Take(8)
            .ToList();

        if (rejected.Count > 0)
            recorder.Write("nothing-picked", new { rejected });
    }

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
            observations.RecordKill(mob.BNpcNameId, area.TerritoryTypeId);

            recorder?.Write("kill", new
            {
                n = kills,
                took = engagedAt.Remove(id, out var since)
                    ? Math.Round((DateTime.UtcNow - since).TotalSeconds, 1)
                    : (double?)null,
                spot = spotIndex % Math.Max(1, area.Spots.Count),
            });
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
