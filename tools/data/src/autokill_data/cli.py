"""Command line entry point for the data extractor."""

from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any

from .build import EXPANSIONS, Mob, build, to_plugin_json
from .sources import Cache, teamcraft

ROOT = Path(__file__).resolve().parents[2]
CACHE_DIR = ROOT / ".cache"
OUT_DIR = ROOT / "out"


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
    lookup = sub.add_parser("lookup", help="show where an item can be farmed")
    lookup.add_argument("item")
    lookup.set_defaults(func=cmd_lookup)

    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
