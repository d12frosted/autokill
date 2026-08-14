# 0012. Hunt bills come from the client

## Status

Accepted

## Context

A hunt bill is already a farming order written by the game: kill three of this mob, in
that place. It is the one thing this plugin works with that nobody had to guess at.

Everything needed is in the client. `MobHuntOrder` holds the targets of each bill, and its
target rows point at `BNpcName` row ids, which is the key the mob index is already built
on. Each target names its `Map`, so the zone is known rather than inferred.
`UIState.MobHunt` holds which bills are in hand and how many of each target have been
killed.

Two things need deciding: which bills to offer, and what to do about a target mob that
also lives somewhere the bill did not name.

Bills come in two kinds. The ordinary ones name five targets, three kills each for the
common mobs and one for the named ones. The weekly elite bill names a single mark. Every
elite bill target in the sheet is rank 1 in `NotoriousMonster`, which is a B rank: there
are no A or S ranks on a bill, and a B rank is a mob one player is expected to kill.

Coverage was measured against the shipped position data, by target and by the map the
bill itself names:

| bill kind | targets | positions on the named map |
|---|---|---|
| elite (B ranks) | 77 | 74 |
| ordinary | 1,195 | 1,051 |

## Decision

Read bills from the client every time the tab is drawn rather than caching. The counts
change while a run is going, and a stale count is worse than a cheap read.

Offer both kinds. An elite bill is marked as such in the window, since one mark killed
once behaves nothing like three of something common.

Offer only areas inside the territory the bill names, and say "nowhere recorded in that
zone" rather than falling back to the same mob elsewhere.

Set the kill goal to what is left on the target, and set nothing else.

Validate the order row the client hands back against the range its bill type owns. A row
outside it is logged and the bill skipped.

## Consequences

- Kill counts are the game's own, so they are right about kills the plugin had nothing to
  do with: the bill picks up where it was left, whoever did the killing.
- Elite bills are the best served of the lot. A mark stands in a handful of known places
  rather than wherever a field happens to spread, so the circuit between those places is
  exactly what hunting one looks like by hand: go round the spawn points until it is
  there.
- A mark that is not up means a run that patrols until it is, or until stopped. That is
  the correct behaviour and the reason a time limit is worth setting on one.
- Nothing here identifies a mob by anything but its `BNpcName` id, which every battle NPC
  carries, so marks are found by the same code as everything else despite having no
  `BNpcBase` id in the spawn data.
- Refusing to fall back to another zone means a bill sometimes offers nothing. That is
  correct: kills in the wrong zone count for nothing towards the bill, so travelling there
  would be worse than doing nothing.
- The goal is only ever kills, because a bill is finished by killing. A time limit or a
  drop target could only stop a run before the bill was done.
- The range check is there because the row id comes from a signature-scanned function.
  If a game patch moves it, the failure is a skipped bill and a warning rather than a
  character sent to the wrong end of the world.
