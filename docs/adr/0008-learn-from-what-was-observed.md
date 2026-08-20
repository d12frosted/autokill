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

Three measurements, from the three things a run does in a field.

Standing on one and watching it: we cleared it, it went quiet, something is standing there
again. No travel in that number, and it is the only measurement a mob with a single spot
can produce, since such a run never leaves. Only what we emptied ourselves counts; a spot
already quiet on arrival may have been quiet for an hour, and timing from our arrival
would be timing the walk to it.

Leaving one and coming back: emptying stamps the time, finding it populated again closes
the loop. That number carries the whole trip back inside it, so it reads long. It is worth
keeping anyway, because the question a circuit asks is whether coming back is worth it,
not when the server ticked.

Timing single spawn points: one thing went down here, and later something else was standing
here. Neither end needs the spot around it to be quiet, which is what makes it the only
measurement a field busy enough to never empty can produce. It is timed from where a mob
was first seen rather than from where it fell, because a pull walks it in to the character
and the ground it dies on belongs to nothing.

Only that third one is close enough to a respawn to be called one, and the estimate prefers
it as soon as there are enough of them. Until then the return trips carry it, since an
inflated number still beats the flat fallback.

Everything rests on having watched both ends. A death too far off is a draw distance rather
than a kill, a death left behind when the circuit moves on comes back where nobody is
looking, and a gap in the looking at all could hide any amount of time. All three are
thrown away rather than guessed at.

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
- The places farmed hardest used to learn the least, and the two spot measurements only
  half fixed it. A mob with a single spot is covered by watching the spot underfoot, since
  such a run never leaves. A wide field is not: it never comes back inside the ten minute
  guard, and the few seconds of patience before the circuit moves on is far short of a
  respawn, so the watch is called off every time. Hundreds of kills, nothing learnt.
  Timing spawn points covers exactly that case, and produces a measurement every time
  anything comes back rather than once a circuit.

- The estimate is worth least where it used to be missing. A wide field leaves every spot
  sitting far past any plausible respawn, so readiness pins at its ceiling and the pick
  comes down to size and distance whatever the estimate says. It bites on short circuits
  over slow ground, which is where the return trips already reached. Timing spawn points
  is therefore mostly an honesty fix: the tab now says what the ground is doing in the
  places it is farmed hardest.
- Sightings are collected but not yet used to move spot positions. Measurement showed
  shipped spot centres sit within 4 to 13 yalms of observed centres, which is not enough
  error to justify moving them. The data is kept so the question can be revisited with
  evidence rather than intuition.
