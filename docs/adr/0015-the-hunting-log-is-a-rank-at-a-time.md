# 0015. The hunting log is a rank at a time

## Status

Accepted

## Context

The hunting log is the game's own standing order to go and kill things: fifty entries
for each class in five ranks of ten, thirty for each Grand Company in three, a few kills
of a named mob on every entry, and a lump of experience for finishing a rank. It is the
same kind of list as a hunt bill (0012), written by the game rather than guessed at. It
differs in three ways that matter, and every decision below comes from one of them: only
the class the log belongs to gets credit for the kill, the entries are tiny and there are
hundreds of them, and the whole thing is ordered by level.

Everything needed is in the client. `MonsterNote` holds the entries, `MonsterNoteTarget`
the mobs each names and the zone each mob stands in, and those rows point at `BNpcName`
ids, which is the key the mob index is already built on. Nothing in either sheet says
which class an entry belongs to. The row id does, and the client's own agent builds it as
`ClassId * BaseId + Rank * 10 + index + 1`: `ClassJob` row id times ten thousand for a
class, Grand Company row id times a million for the other three, ten entries to a rank,
counted from zero. That is the encoding rather than a guess about it.

Progress is in `MonsterNoteManager`: twelve logs, and for each of them the rank it is on,
its ten entries, and a kill count per mob of up to four. Only the rank it is on. A rank
opens when the one before it is done, so the rank the client is carrying is the only rank
there is anything to do about.

Coverage was measured against the shipped position data, by mob and by the zone the log
itself names. `uv run autokill-data hunting-log` prints it:

| log | entries | kills | mobs | positioned | in a named zone | entries reachable |
|---|---|---|---|---|---|---|
| each of the nine classes | 50 | 166-265 | 59-68 | all | all | 50 |
| Maelstrom | 30 | 92 | 30 | 29 | 23 | 23 |
| Order of the Twin Adder | 30 | 101 | 30 | 29 | 22 | 22 |
| Immortal Flames | 30 | 89 | 30 | 29 | 22 | 22 |

Every mob on every class log stands somewhere the data records, inside the zone the log
names. That is 450 entries out of 450, and nothing else this plugin works with is covered
like that. The 23 misses are all the same thing: the Grand Company logs send you into
Halatali, Cutter's Cry, the Sunken Temple of Qarn and the Wanderer's Palace, and a
dungeon is not somewhere a run can go.

Two more things were measured, because they decide how a run should be shaped.

A rank is ten entries of three or four kills, which run one at a time is ten teleports
for thirty-odd kills. On average a rank holds 9.6 reachable entries spread over 7.4
zones, but 4.8 zones is enough to cover all of them, and 397 of the 517 entries stand
within 250 yalms of another entry of their own rank, which is the radius that already
makes one field.

Where an entry sits in a class log is the level it was written for: entry 11 is the
level 11 entry. Against the recorded level of the ground it sends you to, that is right
within three levels for 416 of the 439 targets that can be compared, mean +0.3. Unlike
the recorded levels, which are missing for a quarter of the targets, it is there for
every entry of every class log. A Grand Company log has no such ordering.

## Decision

Read the entries from the sheets and the counts from the client every time the tab is
drawn, without caching, for the same reason as 0012: the counts move while a run is
going.

Offer the rank the client says each log is on, and nothing else. Ranks behind it are
done and ranks ahead of it cannot be started.

The class stops being a preference and becomes the requirement. 0014 picks the gearset
that will clear the field fastest; here the log names who has to land the kill, so the
gearset for that class is put on instead, and a log with no gearset for its class is
shown as such rather than offered. Being under-levelled for the ground is no longer a
reason to change job, since changing job is exactly what must not happen, so the standing
instruction narrows to going anyway or refusing.

Show the logs that are open, and say plainly what is in the way of the others rather
than listing all twelve alike. A class at level zero has never been picked up and has no
log to fill in; a class with a log but no gearset cannot be worn into one. The two read
identically as "nothing on offer" unless they are named, and they are different errands:
one is a trip to a guild, the other a minute in the character sheet. Closed logs are
dimmed and sorted below the open ones rather than hidden, since what a log wants is worth
reading before going to unlock it. Of the Grand Company logs, only the one you belong to
is shown, which is also all the game shows.

Offer only entries whose mobs stand in a zone the log names, and say "nowhere recorded in
that zone" for the rest, exactly as a bill does. Whether a kill somewhere else counts
towards the log is not something this can find out without testing it, and a trip that
turns out not to count is worse than a trip not taken.

Farm a rank by the ground rather than by the entry. The mobs of a rank are grouped into
the fewest zones that hold all of them, each zone is then broken into the fields inside
it, since a zone with two of them at opposite ends is two places to stand, and each field
becomes one leg of the queue that already carries a crafting list. A leg goes after every
log mob standing in its field at once, which is what 0013 made runs able to do. The stop
condition is one kill target per mob, all of them, set from what the client says is still
owed when that leg starts rather than when the route was drawn up.

A run that counts mobs separately goes after exactly the mobs it still counts, checked
when it starts and again on every kill. This is the one place where going after a set of
mobs needs more than 0013 gave it. An item run wants the whole field for as long as it
lasts; these targets fill up one at a time, and the field holds all of them either way.
A mob left in the quarry after its own count is full keeps the ground looking busy, keeps
the circuit from ever judging a spot cleared, and holds the run in one place killing what
it already had enough of. A mob that had no count when the run arrived, because a stop
earlier in the list finished it or because a run picked back up after a death dropped it,
is worse: there is no number behind it at all, so killing it would never end.

Cap what a class log will reach for at that class's own level plus a number, defaulting
to three. The level of an entry is where it sits in the log. An entry above the cap is
still listed and still farmable one at a time, since naming an entry is asking for it,
but "farm the rank" leaves it alone and says why. The cap is worked out again for every
stop, because the class levels while the run goes and entries open up underneath it.

The cap is only for class logs. A Grand Company log pins no class, so whatever suits the
field is put on and the level check that was already there is the thing standing between
a run and ground above it. Its entries still show a level, taken from the ground they
send you to, because the log itself has no ordering to read one out of.

## Consequences

- The counts are the game's own, so a rank picks up wherever it was left, including
  kills the plugin had nothing to do with.
- A rank costs about five teleports instead of ten, and three quarters of the entries
  are killed alongside another entry rather than on their own trip.
- The whole log is not a single button. Finishing a rank unlocks the next one, so the
  next rank is a fresh plan against fresh counts, and that is one press per rank.
- A Grand Company log can be farmed up to its first dungeon entry and no further, since
  a rank that cannot be finished never opens the next one. The Maelstrom's is entry 2.
  They are still worth offering: everything else in the rank is farmable, and an entry
  saying why it cannot be reached is more use than a log that quietly goes missing.
- A class nobody has a gearset for cannot be farmed at all, because the game offers no
  way to equip a bare class. That is the same limitation 0014 already lives with, felt
  harder here, since the whole point of a class log is a class you have barely played.
- Ranks 1 and 2 are levels 1 to 20, where there is no mount and no flying. The circuit
  is walked, the pace is bad, and the estimates say so honestly rather than being
  adjusted for it.
- Nothing here can tell whether a kill in the wrong zone would have counted. If it turns
  out that it would, the rule above is a line to relax, not a design to redo.
- This is the first thing the plugin does that is worth doing for the experience rather
  than for what drops. A character grinding a starter zone on a level 11 lancer is also
  the most conspicuous thing it could be doing.
