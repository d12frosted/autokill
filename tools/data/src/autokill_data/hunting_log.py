"""The hunting log, and whether the plugin knows where to find what it asks for.

The log is the one list in this project that comes entirely out of the game's
own sheets. `MonsterNote` holds the entries, `MonsterNoteTarget` holds the mobs
and the places they stand, and neither needs a community dataset to be read.
What does need one is the answer to "where exactly", which is the same question
everything else here asks and the same two sources answer it.

Nothing in the sheet says which class an entry belongs to. The row id does:

    10001..10050    Gladiator, the fiftieth entry being the last of rank 5
    20001..20050    Pugilist
    ...
    1000001..       Maelstrom, and the other two Grand Companies after it

which is `ClassJob` row id times ten thousand for a class, and Grand Company row
id times a million for the other three. Fifty entries in ten-entry ranks, except
the Grand Company logs, which fill thirty of their fifty rows and leave the rest
empty.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Iterable

CLASS_LOG_STRIDE = 10_000
GRAND_COMPANY_LOG_STRIDE = 1_000_000
ENTRIES_PER_RANK = 10

# Where a class log entry sits is the level it was written for: entry 11 is the
# level 11 entry. Measured against the recorded levels of the ground it sends
# you to, that is right within three levels for 95% of them, and unlike the
# recorded levels it is there for every entry.
LEVEL_IS_THE_INDEX = True


@dataclass(frozen=True, slots=True)
class LogTarget:
    """One mob the log asks for, and where it says the mob stands."""

    row_id: int
    bnpc_name_id: int
    zones: tuple[int, ...]
    locations: tuple[int, ...]


@dataclass(frozen=True, slots=True)
class LogEntry:
    """One line of the log: some mobs, and how many of each."""

    row_id: int
    name: str
    class_job_id: int
    grand_company_id: int
    index: int
    reward: int
    kills: tuple[tuple[int, int], ...]

    @property
    def rank(self) -> int:
        return (self.index - 1) // ENTRIES_PER_RANK + 1

    @property
    def total_kills(self) -> int:
        return sum(count for _, count in self.kills)

    @property
    def log(self) -> str:
        """The log this entry belongs to, named the way the sheet names it."""
        return self.name.rsplit(" ", 1)[0]

    @property
    def level(self) -> int | None:
        """
        The level the entry was written for, or nothing when the log does not
        say. A Grand Company log's thirty entries do not climb one level at a
        time, so only the class logs answer this.
        """
        if self.grand_company_id:
            return None
        return self.index


@dataclass(frozen=True, slots=True)
class TargetCoverage:
    """One mob of one entry, and how much is known about where it stands."""

    entry: LogEntry
    target: LogTarget
    spots: int
    named_spots: int
    territories: tuple[int, ...]

    @property
    def covered(self) -> bool:
        return self.named_spots > 0


@dataclass(frozen=True, slots=True)
class LogCoverage:
    """What is known about one whole log."""

    log: str
    entries: int
    reachable_entries: int
    targets: int
    positioned_targets: int
    named_targets: int


def _int(value: Any) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return 0


def parse_targets(rows: Iterable[dict[str, Any]]) -> list[LogTarget]:
    out: list[LogTarget] = []
    for row in rows:
        bnpc_name_id = _int(row.get("BNpcName"))
        if bnpc_name_id == 0:
            continue
        out.append(
            LogTarget(
                row_id=_int(row.get("#")),
                bnpc_name_id=bnpc_name_id,
                zones=tuple(
                    z
                    for z in (_int(row.get(f"PlaceNameZone[{i}]")) for i in range(3))
                    if z
                ),
                locations=tuple(
                    p
                    for p in (_int(row.get(f"PlaceNameLocation[{i}]")) for i in range(3))
                    if p
                ),
            )
        )
    return out


def parse_entries(rows: Iterable[dict[str, Any]]) -> list[LogEntry]:
    out: list[LogEntry] = []
    for row in rows:
        row_id = _int(row.get("#"))
        if row_id == 0:
            continue

        kills = tuple(
            (target, count)
            for target, count in (
                (
                    _int(row.get(f"MonsterNoteTarget[{i}]")),
                    _int(row.get(f"Count[{i}]")),
                )
                for i in range(4)
            )
            if target and count
        )
        # The Grand Company logs are thirty entries in a fifty entry shape, and
        # the rows past the end are still there, asking for nothing.
        if not kills:
            continue

        grand_company_id, class_job_id = divmod(row_id, GRAND_COMPANY_LOG_STRIDE)
        if grand_company_id:
            class_job_id, index = 0, class_job_id
        else:
            class_job_id, index = divmod(row_id, CLASS_LOG_STRIDE)

        out.append(
            LogEntry(
                row_id=row_id,
                name=row.get("Name", ""),
                class_job_id=class_job_id,
                grand_company_id=grand_company_id,
                index=index,
                reward=_int(row.get("Reward")),
                kills=kills,
            )
        )
    return out


def measure(
    entries: Iterable[LogEntry],
    targets: dict[int, LogTarget],
    spawns: dict[int, dict[int, int]],
    territory_place: dict[int, int],
) -> list[TargetCoverage]:
    """
    How well the shipped position data covers what the log asks for.

    `spawns` is spawn point counts per mob per territory, both halves of the
    plugin's data merged, and `territory_place` is each territory's `PlaceName`,
    which is what turns "Central Shroud" in the sheet into somewhere to go.
    """
    out: list[TargetCoverage] = []
    for entry in entries:
        for target_id, _ in entry.kills:
            target = targets.get(target_id)
            if target is None:
                continue

            seen = spawns.get(target.bnpc_name_id, {})
            wanted = set(target.zones)
            named = {
                territory: count
                for territory, count in seen.items()
                if territory_place.get(territory, 0) in wanted
            }
            out.append(
                TargetCoverage(
                    entry=entry,
                    target=target,
                    spots=sum(seen.values()),
                    named_spots=sum(named.values()),
                    territories=tuple(sorted(named)),
                )
            )
    return out


def summarise(coverage: Iterable[TargetCoverage]) -> list[LogCoverage]:
    """One row per log, in the order the sheet lists them."""
    order: list[str] = []
    entries: dict[str, dict[int, list[TargetCoverage]]] = {}

    for row in coverage:
        log = row.entry.log
        if log not in entries:
            entries[log] = {}
            order.append(log)
        entries[log].setdefault(row.entry.row_id, []).append(row)

    out: list[LogCoverage] = []
    for log in order:
        by_entry = entries[log]
        rows = [row for group in by_entry.values() for row in group]
        out.append(
            LogCoverage(
                log=log,
                entries=len(by_entry),
                reachable_entries=sum(
                    1 for group in by_entry.values() if all(r.covered for r in group)
                ),
                targets=len(rows),
                positioned_targets=sum(1 for r in rows if r.spots),
                named_targets=sum(1 for r in rows if r.covered),
            )
        )
    return out


@dataclass(frozen=True, slots=True)
class RankShape:
    """What one rank of ten entries would cost to run."""

    log: str
    rank: int
    entries: int
    zones: int
    trips: int
    paired_entries: int


def rank_shape(
    coverage: Iterable[TargetCoverage],
    points: dict[int, dict[int, list[tuple[float, float]]]],
    radius: float,
) -> list[RankShape]:
    """
    How many places a rank actually sends you to.

    A rank is ten entries of a few kills each, so run one entry at a time it is
    ten teleports for thirty-odd kills. Two savings are available. Entries can
    be grouped by zone, and `trips` is how many zones it takes to cover all of
    them. Within a zone, entries whose mobs stand within `radius` of each other
    are one field rather than two, and `paired_entries` counts the entries that
    have at least one such neighbour.
    """
    order: list[tuple[str, int]] = []
    entries_seen: dict[tuple[str, int], set[int]] = {}
    # Which mobs of which entries stand in which territory, once per rank.
    where: dict[tuple[str, int], dict[int, list[tuple[int, int]]]] = {}

    for row in coverage:
        if not row.covered:
            continue
        key = (row.entry.log, row.entry.rank)
        if key not in where:
            where[key] = {}
            entries_seen[key] = set()
            order.append(key)
        entries_seen[key].add(row.entry.row_id)
        for territory in row.territories:
            where[key].setdefault(territory, []).append(
                (row.entry.row_id, row.target.bnpc_name_id)
            )

    out: list[RankShape] = []
    for key in order:
        log, rank = key
        zones = where[key]
        out.append(
            RankShape(
                log=log,
                rank=rank,
                entries=len(entries_seen[key]),
                zones=len(zones),
                trips=_trips(zones, entries_seen[key]),
                paired_entries=len(_paired(zones, points, radius)),
            )
        )
    return out


def _trips(
    zones: dict[int, list[tuple[int, int]]], entries: set[int]
) -> int:
    """
    The fewest zones that between them hold every entry of the rank.

    Greedy, which is not always the true minimum, but a rank is ten entries
    over a handful of zones and greedy is exact on nearly all of them.
    """
    left = set(entries)
    chosen = 0
    holds = {
        territory: {entry for entry, _ in occupants}
        for territory, occupants in zones.items()
    }
    while left:
        best = max(holds.values(), key=lambda held: len(held & left), default=set())
        gained = best & left
        if not gained:
            break
        left -= gained
        chosen += 1
    return chosen


def _paired(
    zones: dict[int, list[tuple[int, int]]],
    points: dict[int, dict[int, list[tuple[float, float]]]],
    radius: float,
) -> set[int]:
    """Entries standing within `radius` of another entry of the same rank."""
    out: set[int] = set()
    for territory, occupants in zones.items():
        for i in range(len(occupants)):
            for j in range(i + 1, len(occupants)):
                if occupants[i][0] == occupants[j][0]:
                    continue
                if _within(occupants[i][1], occupants[j][1], points, territory, radius):
                    out.add(occupants[i][0])
                    out.add(occupants[j][0])
    return out


def _within(
    one: int,
    other: int,
    points: dict[int, dict[int, list[tuple[float, float]]]],
    territory: int,
    radius: float,
) -> bool:
    radius_sq = radius * radius
    here = points.get(one, {}).get(territory, [])
    there = points.get(other, {}).get(territory, [])
    for x1, z1 in here:
        for x2, z2 in there:
            dx, dz = x1 - x2, z1 - z2
            if dx * dx + dz * dz <= radius_sq:
                return True
    return False


def level_agreement(
    coverage: Iterable[TargetCoverage],
    levels: dict[int, dict[int, list[int]]],
) -> tuple[int, int, float]:
    """
    How far the level an entry is written for is from the ground it sends you to.

    Returns how many targets could be compared at all, how many of those agree
    within three levels, and the mean difference. Only the class logs have a
    level to compare, and only the embedded half of the position data records
    one, so this is measured on the overlap.
    """
    offsets = []
    for row in coverage:
        if not row.covered or row.entry.level is None:
            continue
        seen = [
            level
            for territory in row.territories
            for level in levels.get(row.target.bnpc_name_id, {}).get(territory, [])
            if level
        ]
        if not seen:
            continue
        offsets.append(max(seen) - row.entry.level)
    if not offsets:
        return 0, 0, 0.0
    return len(offsets), sum(1 for d in offsets if abs(d) <= 3), sum(offsets) / len(offsets)
