"""The dense spawn positions the plugin embeds.

LuminaSupplemental knows where to find far more mobs than anyone else, but it
knows very little about each one: a median of two recorded points, which is not
enough to work out where to stand. Teamcraft covers fewer mobs and covers them
properly, averaging fifteen points and reaching thirty or more for the ones
people actually farm.

So the plugin ships both. This produces the second half: map coordinates keyed
by BNpcName, left unclustered on purpose, because clustering has to happen after
the two sources are merged rather than within either one.
"""

from __future__ import annotations

import json
from typing import Any

FORMAT_VERSION = 1


def extract_positions(
    monsters: dict[str, Any], maps: dict[str, Any]
) -> dict[str, list[list[float]]]:
    """Map coordinates per mob, as [mapId, x, y] triples."""
    out: dict[str, list[list[float]]] = {}

    for key, record in monsters.items():
        points: list[list[float]] = []

        for position in record.get("positions") or []:
            # FATE spawns exist only while their FATE is up, so they are no use
            # as somewhere to go and farm.
            if position.get("fate"):
                continue

            map_id = int(position["map"])
            map_info = maps.get(str(map_id))
            if not map_info or map_info.get("dungeon") or map_info.get("housing"):
                continue

            points.append([map_id, round(float(position["x"]), 1), round(float(position["y"]), 1)])

        if points:
            out[key] = points

    return out


def to_payload(positions: dict[str, list[list[float]]]) -> bytes:
    return json.dumps(
        {"version": FORMAT_VERSION, "positions": positions}, separators=(",", ":")
    ).encode()
