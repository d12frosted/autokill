# 0019. A FATE mob is never fought

## Status

Accepted

Supersedes the FATE targets of [0012](0012-hunt-bills-come-from-the-client.md).

## Context

A run finds its quarry by `BNpcName`, and a FATE spawns mobs under the same names as the
open world ones around it. A FATE in the middle of a yak field fills the field with more
Wooly Yaks, and the run picked those as readily as the rest. Above the FATE's level
nothing lands on them until the character syncs down, so the run stood beside one while
every cast failed with "your level is too high", until the stall watch gave up on it and
it picked the next one, which was often from the same FATE.

Syncing down is the only way to fight one, and it is the wrong trade for a farming run. A
synced character is weaker and slower to kill anything, and a FATE is a crowd of mobs at
the level it was synced down to. Even when a FATE mob drops what the run wants, the open
world ones it shares a name with are the cheaper way to get it.

0012 offered a hunt bill target that lives inside a FATE while that FATE was up, and sent
the run to where the FATE stood. That is the same fight with the same sync.

## Decision

A mob with a FATE id is never a quarry. The id is read from the client's `GameObject`,
since Dalamud does not carry it, and it is judged alongside dead, not a combatant and
someone else's fight, so a recording of what was passed over says "in a FATE".

A FATE mob is also no part of the field: it is not counted as in sight, its respawns are
not timed and its sightings are not learnt from. The shipped position data already
leaves FATE spawns out for the same reason.

A hunt bill target that lives inside a FATE is shown, says which FATE, and is not
offered.

## Consequences

- A field with a FATE in it farms exactly as it would without one, with the FATE's mobs
  walked past like scenery.
- Bill targets that only exist inside a FATE cannot be farmed from here at all. They are
  96 of 811, all on ordinary bills, and finishing one means doing the FATE by hand.
- Nothing needs the FATE table any more, so the plugin no longer asks for it.
