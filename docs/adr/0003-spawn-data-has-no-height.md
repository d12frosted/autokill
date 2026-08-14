# 0003. Spawn data has no height

## Status

Accepted

## Context

`MobSpawnPosition.Position` is a three component vector, which invites reading it as a
point in space. It is not one.

The first two components are map coordinates, the numbers the game shows in its position
readout. The third was initially read as world elevation. Checking 3,200 random rows
against each map's own size factor showed the third component equals the *second* one
converted to world coordinates on 81.6% of rows, and the remainder differ only where map
offsets are involved. It is a duplicate, not a dimension.

Reading it as height put every spot hundreds of yalms below the floor. One run took off,
hovered, and never moved: its destination was 690 yalms underground, the navmesh floor
query found nothing there, and the fallback kept the bad height, so vnavmesh accepted a
path it could never fly and reported no error at all.

## Decision

Treat published spawn positions as two dimensional. Convert X and Y through the map
projection, discard the third component, and leave height at zero through the index.

Resolve the ground at the point of use by dropping onto the navmesh from `Y = 1024`, the
same trick vnavmesh uses to turn a map flag into a point. Starting a floor query from
zero asks about the middle of the world, which in most zones is solid rock or open sky.

A failed floor query is never cached. It usually means the mesh is still loading, and
caching the fallback strands a run at a point it can never reach for as long as it lasts.

## Consequences

- Spot positions are only meaningful once a navmesh exists for the zone, which the travel
  phase already waits for.
- The map projection is needed regardless of source, since both halves of the data are
  map projected. The conversion has its own tests pinned to two anchors: map 21.5 is world
  zero, and map 1.0 is world -1024.
- Any future source claiming to carry elevation should be verified against observed player
  positions before it is believed.
