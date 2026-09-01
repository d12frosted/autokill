# 0018. The fight borrows the feet

## Status

Accepted

## Context

0001 gave movement to vnavmesh and fighting to a rotation plugin, and neither of them
steps out of anything. vnavmesh walks a route to a place; a rotation presses buttons.
Between them there is nobody watching the ground, so a run stands in every cast that
lands on it. On overworld trash a person notices this once a fight is long enough for
the mob to finish a cast, which is exactly what an under-levelled hunting log class is
doing all afternoon.

BossMod already solves it, and does not need a boss module to. Its hint builder falls
back to reading arbitrary enemy casts, guessing the shape from the action's own cast
type and effect range, and putting a forbidden zone under it. Its pathfinder then walks
around what it drew. In a field that is the whole of the danger.

BossMod and BossMod Reborn are forks of each other and register the same `BossMod.` IPC
names, so only one can be loaded and either of them serves.

What BossMod exposes over IPC is presets, not the AI toggle. A preset is a set of
autorotation modules with tracks, and one of those modules, `MiscAI.NormalMovement`, is
the character's feet: `Destination` set to `Pathfind` is the dodging. That is a narrower
handle than the AI toggle and a better one, because a preset carrying nothing but that
module moves the character without pressing a single button, and the buttons are Wrath's.

The cost is that movement now has two owners, and they must never both be driving.

## Decision

Lend the feet to BossMod for exactly as long as the run is standing still to fight, and
take them back the moment it is not.

That window already exists and is already the shape of this decision: a run that has
reached its quarry stops the route and stands there letting the rotation work. Travel is
vnavmesh's and stays vnavmesh's, because BossMod's pathfinding is combat positioning
rather than crossing a zone.

Reconcile the handover once at the end of every tick, from a flag the fighting branch
sets, rather than releasing at each of the dozen ways a fight ends. Every one of them
comes back through the tick.

While the feet are lent out, do not steer. A dodge is by definition a walk away from
where the run put the character, and the run's own rule is that anything out of reach is
walked back to, so the two left alone would take turns: BossMod stepping out of a cast
and vnavmesh routing straight back into it. Out of reach while BossMod is driving is
therefore left alone up to a distance no dodge would cover, and the same module walks
back into range once the ground is clear. Further off than that is a quarry that has
wandered rather than a dodge, and it takes the feet back with it.

Activate a preset of this plugin's own making, holding `NormalMovement` alone, with
`Destination` on `Pathfind` and `Range` on `MaxRange`. Range is not incidental: it keeps
a caster at the far edge of its own reach instead of walking it into melee, which is both
safer and how a person plays.

Put back whatever preset was active, because activating one clears what was running, and
delete this plugin's preset when it unloads. A preset left in somebody's list is a
lasting change to their configuration, which is the line 0017 draws around Wrath and the
same line belongs here.

Treat BossMod's feet as the run's own for the purposes of noticing that the player has
taken the controls (`PilotWatch`). Movement nobody asked for is the signal that somebody
is at the keyboard, and a dodge is movement this run asked for.

Optional, like Lifestream. Without it a run fights exactly as it did before, and the
needs panel says what is missing rather than leaving it as a character that dies for no
visible reason.

## Consequences

- Movement has two owners, split by state rather than by negotiation. The two never
  overlap, and the two places that could confuse them, the watch for a player taking the
  controls and the rule about walking back to something out of reach, are both told
  about it.

- Range is now decided twice over: by this plugin when it walks up to a mob, and by
  BossMod while the fight is on. They do not have to agree, they only have to be within
  a dodge of each other, which for every role they are.
- BossMod's autorotation presets are the handle, so a player who had a preset active has
  it cleared for the length of each fight and put back after. A multi-preset setup, which
  only upstream BossMod supports, comes back as whichever single one was reported.
- The dodging is only as good as BossMod's guess at a cast, which for overworld mobs is
  read off the action sheet rather than written by hand. Cast types it has no shape for
  are not dodged.
- A run that ends mid-fight keeps the feet along with the rotation until the fighting is
  over, for the same reason it keeps the rotation.
- This is a third plugin's contract discovered at runtime, in a shape (a JSON preset with
  module and track names in it) that is more brittle than a numbered enum. A rename
  upstream reads as BossMod refusing the preset, which is logged and degrades to not
  dodging.
