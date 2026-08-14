"""Join the upstream datasets into one index the plugin can ship.

Two datasets carry the halves we need and neither is enough alone:

  Garland Tools  which mobs drop which items, keyed by a composite mob id whose
                 low ten digits are the game's BNpcName id
  Teamcraft      where mobs have actually been seen standing, keyed by BNpcName

BNpcName is the join column. Everything else (territory, expansion, map
projection) comes from the game's own sheets via the datamining CSVs.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

from .coords import map_to_world
from .ids import decode_garland_mob_id
from .sources import Cache, datamining_csv, garland_mob_docs, garland_mob_index, teamcraft
from .spots import cluster

EXPANSIONS = {
    0: "A Realm Reborn",
    1: "Heavensward",
    2: "Stormblood",
    3: "Shadowbringers",
    4: "Endwalker",
    5: "Dawntrail",
}

# How far apart two spawn points can be and still count as one place to stand.
DEFAULT_CLUSTER_RADIUS = 50.0


@dataclass
class FarmSpot:
    territory_id: int
    map_id: int
    x: float
    z: float
    count: int
    level: int
    expansion: int
    zone: str

    def to_json(self) -> dict[str, Any]:
        return {
            "territory": self.territory_id,
            "map": self.map_id,
            "x": round(self.x, 2),
            "z": round(self.z, 2),
            "count": self.count,
            "level": self.level,
        }


@dataclass
class Mob:
    bnpc_name_id: int
    name: str
    drops: set[int] = field(default_factory=set)
    spots: list[FarmSpot] = field(default_factory=list)
    fate_only: bool = False

    @property
    def farmable(self) -> bool:
        return bool(self.spots)

    def to_json(self) -> dict[str, Any]:
        return {
            "name": self.name,
            "drops": sorted(self.drops),
            "spots": [s.to_json() for s in self.spots],
        }


def _territory_lookup(cache: Cache) -> dict[int, dict[str, Any]]:
    out: dict[int, dict[str, Any]] = {}
    for row in datamining_csv(cache, "TerritoryType"):
        try:
            territory_id = int(row["#"])
        except (KeyError, ValueError):
            continue
        out[territory_id] = {
            "expansion": int(row.get("ExVersion") or 0),
            "place_name": int(row.get("PlaceName") or 0),
            "intended_use": int(row.get("TerritoryIntendedUse") or 0),
        }
    return out


def _place_names(cache: Cache) -> dict[int, str]:
    out: dict[int, str] = {}
    for row in datamining_csv(cache, "PlaceName"):
        try:
            out[int(row["#"])] = row.get("Name", "")
        except (KeyError, ValueError):
            continue
    return out


def build(cache: Cache, cluster_radius: float = DEFAULT_CLUSTER_RADIUS, progress=None) -> dict[str, Any]:
    log = progress or (lambda *_: None)

    log("loading teamcraft data")
    maps = teamcraft(cache, "maps")
    monsters = teamcraft(cache, "monsters")
    mob_names = teamcraft(cache, "mobs")

    log("loading game sheets")
    territories = _territory_lookup(cache)
    place_names = _place_names(cache)

    log("loading garland mob index")
    index = garland_mob_index(cache)

    # A creature that appears in several named sub-areas has several Garland
    # entries. Collapse them onto the BNpcName id, which is what the game and
    # every other dataset use.
    by_bnpc: dict[int, list[dict[str, Any]]] = {}
    for entry in index:
        ref = decode_garland_mob_id(int(entry["i"]))
        by_bnpc.setdefault(ref.bnpc_name_id, []).append(entry)

    log(f"fetching drop tables for {len(index)} garland mob entries")
    docs = garland_mob_docs(
        cache,
        (int(e["i"]) for e in index),
        progress=lambda done, total: log(f"  {done}/{total}"),
    )

    drops_by_bnpc: dict[int, set[int]] = {}
    proper_names: dict[int, str] = {}
    for raw_id, doc in docs.items():
        mob = doc.get("mob") or {}
        ref = decode_garland_mob_id(raw_id)
        drops_by_bnpc.setdefault(ref.bnpc_name_id, set()).update(
            int(i) for i in mob.get("drops") or []
        )
        if mob.get("name"):
            proper_names.setdefault(ref.bnpc_name_id, mob["name"])

    log("building spots")
    mobs: dict[int, Mob] = {}
    for key, record in monsters.items():
        bnpc_name_id = int(key)
        positions = record.get("positions") or []
        if not positions:
            continue

        name = proper_names.get(bnpc_name_id) or (mob_names.get(key) or {}).get("en") or ""
        if not name:
            continue

        mob = Mob(
            bnpc_name_id=bnpc_name_id,
            name=name,
            drops=drops_by_bnpc.get(bnpc_name_id, set()),
        )

        open_world = [p for p in positions if not p.get("fate")]
        mob.fate_only = bool(positions) and not open_world

        # Group by map first: coordinates only mean anything within one map's
        # projection, and two maps can share coordinate values entirely.
        by_map: dict[int, list[dict[str, Any]]] = {}
        for position in open_world:
            by_map.setdefault(int(position["map"]), []).append(position)

        for map_id, group in by_map.items():
            map_info = maps.get(str(map_id))
            if not map_info:
                continue
            if map_info.get("dungeon") or map_info.get("housing"):
                continue
            territory_id = int(map_info.get("territory_id") or 0)
            territory = territories.get(territory_id)
            if not territory:
                continue

            size_factor = int(map_info.get("size_factor") or 100)
            offset_x = int(map_info.get("offset_x") or 0)
            offset_y = int(map_info.get("offset_y") or 0)

            points = [
                (
                    map_to_world(float(p["x"]), size_factor, offset_x),
                    map_to_world(float(p["y"]), size_factor, offset_y),
                )
                for p in group
            ]
            levels = [int(p.get("level") or 0) for p in group]
            zone = place_names.get(territory["place_name"], "")

            for spot in cluster(points, radius=cluster_radius):
                mob.spots.append(
                    FarmSpot(
                        territory_id=territory_id,
                        map_id=map_id,
                        x=spot.x,
                        z=spot.z,
                        count=spot.count,
                        level=max(levels) if levels else 0,
                        expansion=territory["expansion"],
                        zone=zone,
                    )
                )

        mob.spots.sort(key=lambda s: -s.count)
        mobs[bnpc_name_id] = mob

    # Mobs that Garland knows the drops of but nobody has recorded a position
    # for. They are not farmable, but the item search still wants to name them.
    for bnpc_name_id, drops in drops_by_bnpc.items():
        if bnpc_name_id in mobs or not drops:
            continue
        name = proper_names.get(bnpc_name_id) or (mob_names.get(str(bnpc_name_id)) or {}).get("en")
        if name:
            mobs[bnpc_name_id] = Mob(bnpc_name_id=bnpc_name_id, name=name, drops=drops)

    drops_index: dict[int, list[int]] = {}
    for mob in mobs.values():
        for item_id in mob.drops:
            drops_index.setdefault(item_id, []).append(mob.bnpc_name_id)

    return {
        "mobs": mobs,
        "drops_index": drops_index,
        "territories": territories,
        "place_names": place_names,
    }


def to_plugin_json(result: dict[str, Any]) -> dict[str, Any]:
    mobs: dict[int, Mob] = result["mobs"]
    return {
        "version": 1,
        "sources": {
            "drops": "garlandtools.org",
            "positions": "ffxiv-teamcraft",
            "sheets": "xivapi/ffxiv-datamining",
        },
        "mobs": {str(m.bnpc_name_id): m.to_json() for m in mobs.values()},
        "drops": {
            str(item_id): sorted(mob_ids)
            for item_id, mob_ids in sorted(result["drops_index"].items())
        },
    }
