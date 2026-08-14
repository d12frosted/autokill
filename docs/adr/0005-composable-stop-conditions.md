# 0005. Composable stop conditions

## Status

Accepted

## Context

A run needs an end. The obvious ones are a kill count, an amount of an item, and a time
limit, and there is no reason to make those exclusive: farming until either 200 kills or
30 hides, whichever comes first, is a perfectly ordinary thing to want.

Two more endings are not goals at all. Dying and filling the bags both mean the run is
over regardless of what was asked for.

## Decision

Conditions are a list, evaluated as either "stop at the first met" or "keep going until
all are met". Any number of item targets can be set at once, each with its own count.

Death and a full inventory are marked as safety conditions and end a run on their own
terms in either mode. Neither becomes acceptable because a kill target is unmet.

An empty set never stops. Without that rule an all-of set of nothing is vacuously true and
the run ends before it starts.

Item counts are read from the inventory rather than from loot events. That sidesteps the
difference between a new stack and a stack growing, and works for any item, including ones
no drop table has heard of. Counting is against a baseline taken at the start, so what was
already in the bags is not mistaken for progress.

## Consequences

- "Farm until I have 30 of this" works even where the drop data is blind, as long as the
  mob is chosen by hand.
- Progress can be shown against targets, and a run reports which condition ended it.
- Conditions know item ids but not item names, since they live in a project that never
  sees the game. Naming happens where the index is to hand.
