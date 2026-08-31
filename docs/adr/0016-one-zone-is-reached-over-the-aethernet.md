# 0016. One zone is reached over the aethernet

## Status

Accepted

## Context

A run into the Dravanian Hinterlands never started. It stopped on "no attuned aetheryte
in The Dravanian Hinterlands", which was true and useless: the zone holds 521 of the
spawn points shipped with the plugin and there is no crystal standing in any of them.

The game's own sheets say the same thing twice. No row in `Aetheryte` has the Hinterlands
as its territory, and `TerritoryType` for the Hinterlands points at aetheryte 75, which
is Idyllshire's, and Idyllshire is a territory of its own. Every other field zone in the
game answers with an aetheryte that stands in it. This is the only one that does not.

The obvious reading is that the character has to be walked out through a gate, which
would mean a hardcoded point at the edge of Idyllshire, a wait on the loading screen, and
a new phase for a journey that crosses a boundary vnavmesh will not path over, since it
meshes one territory at a time.

That reading is wrong. Idyllshire's aethernet has four destinations, and two of them, the
Prologue Gate and the Epilogue Gate, are recorded in `Aetheryte` as standing in the
Hinterlands rather than in the town. The hop out to one of them *is* the zone transition.
Nothing is walked through and no coordinate has to be guessed at.

The aethernet is not the teleport, though. Teleporting between aetherytes is one call to
`Telepo`, which this plugin makes itself. Going one stop on the aethernet is targeting
the crystal, interacting with it, and picking a line out of the menu that opens.

## Decision

Reach the zone the way the game intends: teleport to Idyllshire, then take the aethernet
to one of the gates, then farm as normal.

The route is read out of the sheets rather than written down here. A zone with no attuned
aetheryte of its own falls back to the one its `TerritoryType` names, and the gates are
the shards of that aetheryte's network whose own territory is the zone being farmed. The
Hinterlands is the only zone that answers this today; a zone laid out the same way in a
future patch needs no code.

The aethernet hop itself is delegated to Lifestream over IPC, in keeping with 0001.
Driving the aetheryte menu is a good deal of interface work for one zone, Lifestream
already does it, and Questionable already leans on it for this exact hop. Lifestream is
optional, like a rotation plugin and unlike vnavmesh: without it the Hinterlands is out
of reach and the window says so by name, and nothing else about the plugin changes.

Both gates are tried, later one first. Nothing reports back on an aethernet hop, so a
gate that did nothing looks exactly like a gate still going; the ask is repeated at a gap
wide enough for one to have played out, and each ask moves on to the next gate, since the
likeliest reason for one doing nothing is that it was never attuned. Which gate is tried
first is a matter of flight time rather than correctness: both land in the zone, and only
73 of those 521 spawn points lie beyond the western gate.

## Consequences

- The run has a new phase, `Crossing`, between the teleport and the flight. It is the
  only phase that runs while the character is in a zone the run is not for.
- Failing to get there is now three different sentences instead of one: no aetheryte at
  all, Lifestream missing before the teleport is paid for, and gates that never landed.
- The timing sits in `AutoKill.Core` as `Crossing` and is tested there, the same shape as
  the trip home in 0009's split. What is left in the plugin is which gates to try, which
  is a sheet lookup.
- The hop is a crystal being interacted with, so it wants the character standing at one.
  A teleport lands at the crystal, so the ordinary path is fine; a run started while
  already in Idyllshire and standing somewhere else in it will not get out, and the
  give-up says to try again from the aetheryte rather than only that it failed.
- A run that sets off from the Hinterlands and is told to return there still cannot: the
  trip home teleports and stops, and a teleport lands in Idyllshire. It says so and stays
  put, which is the behaviour it had before this change.
