# 0008. Learn from what was observed

## Status

Accepted

## Context

Shipped data says where a mob lives. It cannot say how quickly a spot repopulates, and
that is what decides whether a circuit should come back or stand waiting. The first
version waited a fixed number of seconds at an empty spot, which is a guess with no
relationship to the zone being farmed.

Running the plugin observes exactly the missing information for free: it is already
looking at every mob nearby, several times a second.

## Decision

Keep a per character store, keyed by mob and zone, of what farming has taught us:
how long spots took to repopulate, where mobs were seen, and how many were killed.

Emptying a spot stamps the time; finding it populated again closes the loop. That number
is deliberately not called a respawn timer. It includes however long it took to return, so
it reads long. It is the more useful figure anyway, because the question a circuit asks is
whether coming back is worth it, not when the server ticked.

Guards on what is believed:

- gaps over ten minutes are discarded, since a logout or a trip to another zone says
  nothing about respawns and one such gap would poison the median permanently
- the median is used, not the mean, so one slow observation does not move the estimate
- fewer than three observations means no estimate at all, and the circuit falls back to
  ninety seconds

Sightings are recorded with positions rounded to five yalms and counted, so a place seen
fifty times weighs more than one glimpse and the file stays small.

## Consequences

- The second run over the same ground behaves better than the first, and says so: the
  Learned tab shows the estimate and how many observations back it.
- Learnt data changes behaviour, so it must be inspectable and erasable. Hence the tab,
  with per entry and wholesale forgetting.
- Sightings are collected but not yet used to move spot positions. Measurement showed
  shipped spot centres sit within 4 to 13 yalms of observed centres, which is not enough
  error to justify moving them. The data is kept so the question can be revisited with
  evidence rather than intuition.
