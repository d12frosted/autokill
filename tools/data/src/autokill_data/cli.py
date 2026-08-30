"""Command line entry point for the data extractor."""

from __future__ import annotations

import argparse
import gzip
import json
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any

from .build import EXPANSIONS, Mob, build, to_plugin_json
from .coords import map_to_world
from .hunting_log import (
    level_agreement,
    measure,
    parse_entries,
    parse_targets,
    rank_shape,
    summarise,
)
from .positions import extract_positions, to_payload
from .sources import Cache, datamining_csv, supplemental_csv, teamcraft

ROOT = Path(__file__).resolve().parents[2]
CACHE_DIR = ROOT / ".cache"
OUT_DIR = ROOT / "out"
SHIPPED_POSITIONS = ROOT.parents[1] / "AutoKill" / "Data" / "positions.json.gz"


def _log(message: str) -> None:
    print(message, file=sys.stderr)


def _coverage_rows(result: dict[str, Any]) -> list[dict[str, Any]]:
    mobs: dict[int, Mob] = result["mobs"]

    per_expansion: dict[int, dict[str, Any]] = defaultdict(
        lambda: {"mobs": 0, "with_drops": 0, "spots": 0, "items": set()}
    )
    for mob in mobs.values():
        if not mob.farmable:
            continue
        expansion = mob.spots[0].expansion
        row = per_expansion[expansion]
        row["mobs"] += 1
        row["spots"] += len(mob.spots)
        if mob.drops:
            row["with_drops"] += 1
            row["items"].update(mob.drops)

    return [
        {
            "expansion": EXPANSIONS.get(ex, f"ex{ex}"),
            "mobs": row["mobs"],
            "with_drops": row["with_drops"],
            "spots": row["spots"],
            "items": len(row["items"]),
        }
        for ex, row in sorted(per_expansion.items())
    ]


def _print_coverage(result: dict[str, Any]) -> None:
    mobs: dict[int, Mob] = result["mobs"]
    farmable = [m for m in mobs.values() if m.farmable]
    with_drops = [m for m in farmable if m.drops]
    droppable_items = {i for m in mobs.values() for i in m.drops}
    reachable_items = {i for m in with_drops for i in m.drops}

    print()
    print(f"mobs known at all              {len(mobs):>7}")
    print(f"  farmable (have positions)    {len(farmable):>7}")
    print(f"  farmable and drop something  {len(with_drops):>7}")
    print()
    print(f"items dropped by a known mob   {len(droppable_items):>7}")
    print(f"  reachable (mob has a spot)   {len(reachable_items):>7}")
    print()

    header = f"{'expansion':<20}{'mobs':>7}{'w/ drops':>10}{'spots':>8}{'items':>8}"
    print(header)
    print("-" * len(header))
    for row in _coverage_rows(result):
        print(
            f"{row['expansion']:<20}{row['mobs']:>7}{row['with_drops']:>10}"
            f"{row['spots']:>8}{row['items']:>8}"
        )
    print()


def cmd_build(args: argparse.Namespace) -> int:
    cache = Cache(CACHE_DIR)
    result = build(cache, cluster_radius=args.radius, progress=_log)

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    out = OUT_DIR / "autokill-data.json"
    payload = to_plugin_json(result)
    out.write_text(json.dumps(payload, separators=(",", ":")))
    _log(f"wrote {out} ({out.stat().st_size / 1_000_000:.1f} MB)")

    _print_coverage(result)
    return 0


def cmd_positions(args: argparse.Namespace) -> int:
    """Emit the dense spawn positions the plugin embeds."""
    cache = Cache(CACHE_DIR)
    _log("loading teamcraft data")
    positions = extract_positions(teamcraft(cache, "monsters"), teamcraft(cache, "maps"))

    payload = to_payload(positions)
    out = Path(args.out) if args.out else OUT_DIR / "positions.json.gz"
    out.parent.mkdir(parents=True, exist_ok=True)
    # mtime in the gzip header would make every rebuild a different file, which
    # turns a no-op refresh into a commit.
    with gzip.GzipFile(out, "wb", compresslevel=9, mtime=0) as handle:
        handle.write(payload)

    total = sum(len(v) for v in positions.values())
    _log(
        f"wrote {out}: {len(positions)} mobs, {total} positions, "
        f"{out.stat().st_size / 1000:.0f} kB"
    )
    return 0


def cmd_coverage(args: argparse.Namespace) -> int:
    cache = Cache(CACHE_DIR)
    result = build(cache, cluster_radius=args.radius, progress=_log)
    _print_coverage(result)
    return 0


def cmd_lookup(args: argparse.Namespace) -> int:
    cache = Cache(CACHE_DIR)
    result = build(cache, cluster_radius=args.radius, progress=_log)
    mobs: dict[int, Mob] = result["mobs"]

    items = teamcraft(cache, "items")
    needle = args.item.strip().lower()
    matches = [
        int(k)
        for k, v in items.items()
        if (v.get("en") or "").lower() == needle
    ] or [
        int(k)
        for k, v in items.items()
        if needle in (v.get("en") or "").lower()
    ][:5]

    if not matches:
        print(f"no item matching {args.item!r}")
        return 1

    for item_id in matches:
        name = items[str(item_id)]["en"]
        droppers = result["drops_index"].get(item_id, [])
        print(f"\n{name} ({item_id}) - dropped by {len(droppers)} mob(s)")
        for mob_id in droppers:
            mob = mobs[mob_id]
            if not mob.spots:
                print(f"  {mob.name:<34} no known spawn positions")
                continue
            best = mob.spots[0]
            print(
                f"  {mob.name:<34} {best.zone} lv{best.level} "
                f"({best.x:.0f}, {best.z:.0f}) x{best.count} "
                f"[{len(mob.spots)} spot(s)]"
            )
    return 0


def _shipped_points(
    cache: Cache, positions_file: Path
) -> tuple[
    dict[int, dict[int, list[tuple[float, float]]]],
    dict[int, dict[int, list[int]]],
]:
    """Where the plugin thinks every mob stands, in world coordinates.

    Both halves, because the plugin merges both: the dense embedded payload,
    keyed by map, and LuminaSupplemental's wider and thinner one, keyed by
    territory. Both record map coordinates, so both are projected the same way.
    """
    maps: dict[int, tuple[int, int, int, int]] = {}
    for row in datamining_csv(cache, "Map"):
        if not row.get("#", "").isdigit():
            continue
        maps[int(row["#"])] = (
            int(row.get("TerritoryType") or 0),
            int(row.get("SizeFactor") or 100),
            int(row.get("OffsetX") or 0),
            int(row.get("OffsetY") or 0),
        )
    territory_map = {
        int(row["#"]): int(row.get("Map") or 0)
        for row in datamining_csv(cache, "TerritoryType")
        if row.get("#", "").isdigit()
    }

    points: dict[int, dict[int, list[tuple[float, float]]]] = defaultdict(
        lambda: defaultdict(list)
    )
    # Only the embedded half records how hard anything was where it was seen.
    levels: dict[int, dict[int, list[int]]] = defaultdict(lambda: defaultdict(list))

    def record(mob_id: int, map_id: int, territory: int, x: float, y: float) -> None:
        projection = maps.get(map_id)
        if not projection or not territory:
            return
        _, size_factor, offset_x, offset_y = projection
        points[mob_id][territory].append(
            (
                map_to_world(x, size_factor, offset_x),
                map_to_world(y, size_factor, offset_y),
            )
        )

    with gzip.open(positions_file, "rb") as handle:
        embedded = json.load(handle)["positions"]
    for mob_id, rows in embedded.items():
        for row in rows:
            map_id = int(row[0])
            territory = maps.get(map_id, (0,))[0]
            record(int(mob_id), map_id, territory, row[1], row[2])
            if territory and len(row) > 3 and row[3]:
                levels[int(mob_id)][territory].append(int(row[3]))

    for row in supplemental_csv(cache, "MobSpawn"):
        try:
            mob_id = int(row["BNpcNameId"])
            territory = int(row["TerritoryTypeId"])
            x, y, _ = row["Position"].split(";")
        except (KeyError, ValueError):
            continue
        record(mob_id, territory_map.get(territory, 0), territory, float(x), float(y))

    return (
        {mob: dict(seen) for mob, seen in points.items()},
        {mob: dict(seen) for mob, seen in levels.items()},
    )


def cmd_hunting_log(args: argparse.Namespace) -> int:
    """How much of the hunting log the plugin knows where to find."""
    cache = Cache(CACHE_DIR)
    positions_file = Path(args.positions) if args.positions else SHIPPED_POSITIONS

    _log("loading game sheets")
    entries = parse_entries(datamining_csv(cache, "MonsterNote"))
    targets = {t.row_id: t for t in parse_targets(datamining_csv(cache, "MonsterNoteTarget"))}
    territory_place = {
        int(row["#"]): int(row.get("PlaceName") or 0)
        for row in datamining_csv(cache, "TerritoryType")
        if row.get("#", "").isdigit()
    }
    places = {
        int(row["#"]): row.get("Name", "")
        for row in datamining_csv(cache, "PlaceName")
        if row.get("#", "").isdigit()
    }
    names = {
        int(row["#"]): row.get("Singular", "")
        for row in datamining_csv(cache, "BNpcName")
        if row.get("#", "").isdigit()
    }

    _log(f"loading shipped positions from {positions_file}")
    points, levels = _shipped_points(cache, positions_file)
    spawns = {
        mob: {territory: len(pts) for territory, pts in seen.items()}
        for mob, seen in points.items()
    }

    coverage = measure(entries, targets, spawns, territory_place)

    print()
    header = f"{'log':<26}{'entries':>8}{'kills':>7}{'mobs':>6}{'placed':>8}{'in zone':>9}{'entries ok':>12}"
    print(header)
    print("-" * len(header))
    for row in summarise(coverage):
        kills = sum(e.total_kills for e in entries if e.log == row.log)
        print(
            f"{row.log:<26}{row.entries:>8}{kills:>7}{row.targets:>6}"
            f"{row.positioned_targets:>8}{row.named_targets:>9}{row.reachable_entries:>12}"
        )

    shapes = rank_shape(coverage, points, radius=args.radius_field)
    ranks = len(shapes)
    entries_covered = sum(shape.entries for shape in shapes)
    trips = sum(shape.trips for shape in shapes)
    paired = sum(shape.paired_entries for shape in shapes)
    print()
    print(
        f"a rank is {entries_covered / ranks:.1f} reachable entries over "
        f"{sum(shape.zones for shape in shapes) / ranks:.1f} zones, and "
        f"{trips / ranks:.1f} zones is enough to cover all of them"
    )
    print(
        f"{paired} of {entries_covered} entries stand within "
        f"{args.radius_field:.0f} yalms of another entry of their own rank"
    )

    compared, close, mean = level_agreement(coverage, levels)
    if compared:
        print(
            f"the level an entry sits at is within three of the ground it sends "
            f"you to for {close} of {compared} targets (mean {mean:+.1f})"
        )

    unreachable = [row for row in coverage if not row.covered]
    if unreachable:
        print()
        print(f"{len(unreachable)} mob(s) with nothing recorded in the zone the log names:")
        print()
        for row in unreachable:
            zones = ", ".join(places.get(z, "?") for z in row.target.zones)
            where = ", ".join(places.get(p, "?") for p in row.target.locations)
            print(
                f"  {row.entry.name:<28}{names.get(row.target.bnpc_name_id, '?'):<24}"
                f"{zones} ({where}){'' if row.spots else ', nowhere at all'}"
            )
    print()
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="autokill-data")
    parser.add_argument(
        "--radius",
        type=float,
        default=50.0,
        help="how far apart two spawn points can be and still be one farm spot",
    )
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("build", help="build the plugin data file").set_defaults(func=cmd_build)
    sub.add_parser("coverage", help="report how much of the game is covered").set_defaults(
        func=cmd_coverage
    )
    positions = sub.add_parser("positions", help="emit the spawn positions the plugin embeds")
    positions.add_argument("--out", help="where to write the gzipped payload")
    positions.set_defaults(func=cmd_positions)
    hunting = sub.add_parser(
        "hunting-log", help="report how much of the hunting log can be farmed"
    )
    hunting.add_argument("--positions", help="the shipped positions payload to measure against")
    hunting.add_argument(
        "--radius-field",
        type=float,
        default=250.0,
        help="how far apart two mobs can be and still be one field to farm",
    )
    hunting.set_defaults(func=cmd_hunting_log)
    lookup = sub.add_parser("lookup", help="show where an item can be farmed")
    lookup.add_argument("item")
    lookup.set_defaults(func=cmd_lookup)

    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
