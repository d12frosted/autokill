# 0004. Farm areas, not spots

## Status

Accepted

## Context

Clustering spawn points produces spots, and an early version offered each spot as a
separate choice. Farming the almasty in Garlemald meant picking one of five entries of
three, two, two, one and one spawns.

That is not how the field is farmed by hand. The knots are one area, and a player flies a
circuit around it killing what is up, because standing on one knot means waiting on a
respawn timer while the rest of the field is full.

A fixed rotation around the knots is better but still wrong twice over: it returns to
spots emptied moments earlier, and it walks the same loop in the same order all day,
which is exactly what it looks like from outside.

## Decision

Group spots within 250 yalms into an area, using the same single-link clustering that
groups points into spots, and offer the area as the unit of choice. The five almasty
entries become one choice of nine spawns over five spots.

Pick the next spot by readiness rather than by rotation. Score each on how far it has come
towards repopulating, weighted by how many mobs live there, with three rules:

- readiness is capped at one full respawn, so a packed spot emptied seconds ago cannot
  outrank a smaller one that is actually ready
- somewhere never visited outranks everything, since nothing has been taken from it
- close calls are settled by chance, not by a rule

Moving between spots goes back through the travelling phase rather than walking from the
hunt, because travelling already knows how to mount, fly and flag the map.

## Consequences

- The unit the user chooses matches the unit they would have chosen by hand.
- Circuits differ between runs over the same ground, which is both more efficient and less
  mechanical to watch.
- Scoring lives in `AutoKill.Core` with tests, because every rule above is a judgement that
  is easy to get subtly wrong and impossible to check by watching.
