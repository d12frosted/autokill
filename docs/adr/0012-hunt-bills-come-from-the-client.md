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

## Decision

Read bills from the client every time the tab is drawn rather than caching. The counts
change while a run is going, and a stale count is worse than a cheap read.

Offer only ordinary bills, filtered by `MobHuntOrderType.Type`. Elite bills are excluded.

Offer only areas inside the territory the bill names, and say "nowhere recorded in that
zone" rather than falling back to the same mob elsewhere.

Set the kill goal to what is left on the target, and set nothing else.

Validate the order row the client hands back against the range its bill type owns. A row
outside it is logged and the bill skipped.

## Consequences

- Kill counts are the game's own, so they are right about kills the plugin had nothing to
  do with: the bill picks up where it was left, whoever did the killing.
- Elite bills are the B, A and S rank marks. Those are one rare spawn rather than
  something to grind, and sending an unattended character into an S rank has one ending.
  Named marks that appear inside ordinary bills are still offered, since they are
  soloable, though community spawn data covers rare spawns poorly and many of them will
  have nowhere recorded.
- Refusing to fall back to another zone means a bill sometimes offers nothing. That is
  correct: kills in the wrong zone count for nothing towards the bill, so travelling there
  would be worse than doing nothing.
- The goal is only ever kills, because a bill is finished by killing. A time limit or a
  drop target could only stop a run before the bill was done.
- The range check is there because the row id comes from a signature-scanned function.
  If a game patch moves it, the failure is a skipped bill and a warning rather than a
  character sent to the wrong end of the world.
