"""Map coordinates to world coordinates.

Community datasets record spawn points as map coordinates, the numbers the game
shows you in the coordinate readout. vnavmesh wants world coordinates. The
projection between them comes from the Map sheet: a size factor and a per-axis
offset.

    world -> map:  map = 41/c * (((world + offset) * c + 1024) / 2048) + 1
    where c = size_factor / 100

Height is deliberately not converted here. Map data carries elevation
inconsistently and it is not worth trusting; the plugin snaps the resulting XZ
point onto the navmesh floor instead.
"""

_TILE = 2048.0
_HALF_TILE = 1024.0
_MAP_SPAN = 41.0


def map_to_world(value: float, size_factor: int, offset: int) -> float:
    c = size_factor / 100.0
    scaled = _TILE * (value - 1.0) * c / _MAP_SPAN - _HALF_TILE
    return scaled / c - offset


def world_to_map(value: float, size_factor: int, offset: int) -> float:
    c = size_factor / 100.0
    scaled = (value + offset) * c
    return _MAP_SPAN / c * ((scaled + _HALF_TILE) / _TILE) + 1.0
