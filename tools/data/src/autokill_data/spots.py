"""Turn a scatter of spawn points into farm spots.

A mob with 37 recorded positions is not 37 places to go. The points sit in a
handful of loose herds, and what the plugin actually wants is "stand here, mobs
respawn around you". Single-link clustering matches that: two points belong
together if you could walk between them without leaving the pull, and a chain of
such points is one patrol route.
"""

from dataclasses import dataclass, field
from typing import Iterable, Sequence

Point = tuple[float, float]


@dataclass(frozen=True, slots=True)
class Spot:
    x: float
    z: float
    count: int
    points: tuple[Point, ...] = field(default=())


def cluster(points: Iterable[Point], radius: float) -> list[Spot]:
    pts: Sequence[Point] = list(points)
    if not pts:
        return []

    parent = list(range(len(pts)))

    def find(i: int) -> int:
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    def union(a: int, b: int) -> None:
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[max(ra, rb)] = min(ra, rb)

    radius_sq = radius * radius
    for i in range(len(pts)):
        for j in range(i + 1, len(pts)):
            dx = pts[i][0] - pts[j][0]
            dz = pts[i][1] - pts[j][1]
            if dx * dx + dz * dz <= radius_sq:
                union(i, j)

    groups: dict[int, list[Point]] = {}
    for i, p in enumerate(pts):
        groups.setdefault(find(i), []).append(p)

    spots = [
        Spot(
            x=sum(p[0] for p in g) / len(g),
            z=sum(p[1] for p in g) / len(g),
            count=len(g),
            points=tuple(g),
        )
        for g in groups.values()
    ]
    # Densest first, then by position so the output does not depend on input order.
    spots.sort(key=lambda s: (-s.count, round(s.x, 3), round(s.z, 3)))
    return spots
