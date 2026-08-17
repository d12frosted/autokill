# 0013. A run goes after a set of mobs

## Status

Accepted

## Context

A run was one mob and one area. That matches searching by mob name, and it matches a
hunt bill, which counts one mark and nothing else.

It does not match searching by item. Petalouda scales drop from kokkine, kyane and
ianthine petalouda, and all three stand in the same two fields in Elpis. Offered one at
a time, whichever is picked, the run flies past the other two. Worse, an emptied knot is
judged by whether the picked species is back, so the circuit stands waiting on a respawn
timer in a field that is full of the two mobs it was told to ignore.

The stop conditions never had this problem: an item count reads the bags and does not
care what dropped it. Only the targeting did.

## Decision

A run carries a set of mobs and one area. Everything that asked "is this the mob" asks
"is this one of ours" instead.

Fields are built by putting the spots of every wanted mob together and clustering them
again, one territory at a time, with the same 250 yalm radius that makes an area. Spots
within 25 yalms of each other fold into one waypoint, because two species on one knot is
one place to go rather than two, and spawn counts add up: the useful number is how
thickly the ground holds anything worth killing.

By drop and crafting lists offer fields. By mob still offers one mob, because that is
what was asked for, and hunt bills still offer one mob, because only the named mark
counts towards a bill.

What is learnt stays per mob. A kill, a sighting and a repopulation are all credited to
the species they were actually about, so a mixed run does not muddle what is known about
one mob with another sharing its ground. A field's readiness is the soonest any of them
is due back, since a spot with something on it is worth returning to whichever of them is
standing there.

## Consequences

- The field is farmed at the rate the field can give, rather than at one species' share
  of it.
- Runs are named by everything they go after, read out in full. "Three kinds of mob"
  would not say whether the right three were picked.
- History records a list of mobs, and keeps writing the first of them into the old single
  field so a record written now still says something to a version that only knows one.
- Single link clustering chains, so a zone whose fields nearly touch can merge into one
  large field with many spots. That was already true of one mob's areas; putting several
  mobs together makes it likelier. The spot count is shown next to every field, which is
  the honest way to say it.
- Grouping and folding live in `AutoKill.Core` with tests, next to the clustering they
  extend.
