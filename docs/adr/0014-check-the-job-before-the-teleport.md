# 0014. Check the job before the teleport

## Status

Accepted

## Context

A run assumed the character could fight. Two ways that is false, and neither says so.

On a crafter or a gatherer, the run does everything right and nothing happens. It
teleports, mounts, flies to the field, walks up to a mob and stands there, because there
is no rotation to run and nothing to run it with. Wrath is reported as ready, since Wrath
is installed and answering; it just has nothing to do on a Weaver. From the outside this
looks exactly like a broken plugin.

On a battle job well below the field, the run works and the character dies. Dying stops a
run, so this one at least ends, but it ends after the teleport, the flight and the first
pull, and the reason is not stated anywhere.

Levels for the mobs were added in the same change that made this checkable at all. Before
that there was nothing to compare a character against.

## Decision

Check the character against the field's level before starting, and act on one of three
standing instructions:

- **put on a gearset that can** (the default): the highest gearset that clears the
  field's top level, equipped before anything else happens
- **refuse to start, and say why**: nothing is changed, and the reason is on screen
- **go anyway**: says what is wrong and starts regardless

Gearsets rather than jobs, because the game offers no way to equip a bare class, and a
gearset is somebody's own decision about their kit for that job. Levels are read per
experience slot out of the player's record rather than from the gearset, so a gearset
built at 30 and worn to 90 reads as 90.

Which gearset, out of the ones that can manage the field:

1. the job you named, if you named one and it will do
2. something that kills things: melee and ranged first, then a tank, then a healer
3. the highest of those
4. the earliest gearset, so the answer is the same every time

Damage before survivability because the whole job here is emptying a field. A tank clears
one eventually and a healer barely clears it at all, so preferring either would be
preferring the slower run. It is a preference among the gearsets that can already manage
the field, not a reason to send something that will die there.

The named job is a ClassJob rather than a gearset, since two gearsets for one job are the
same answer to "what should I go as". It wins over everything else, because somebody who
named a job named it on purpose, and it is passed over rather than obeyed when it cannot
manage the field.

The comparison is against the top of the field's level range, not the bottom. A field is
patrolled whole, and being able to kill the easiest thing standing in it is not the
question.

An unrecorded level blocks nothing. Three percent of the spawn points carry no level and
302 mobs carry none anywhere, so an unknown level means no opinion rather than level zero.
A crafter is still refused in that case, since that half of the check needs no data.

The window runs the same check while a target is on screen and greys out Start, so the
answer arrives while there is still something to do about it. The controller checks again
when the button is actually pressed, because that is the one moment it must not be wrong.

## Consequences

- Starting a farm can change your job. That is a real side effect and it is the default,
  which is why the setting is a plain three-way choice rather than a checkbox, and why
  every switch is announced.
- The switch is fire and forget. The game takes a moment to swap gear and job over and
  nothing waits for it, because a teleport and a flight follow and both take far longer.
- A gearset change is refused in combat, so a run started mid-fight does not start at all
  and says so, rather than starting on the wrong job.
- Blue Mage counts as a battle job, because it is one. It is capped at 80, so it rarely
  wins the "highest that clears it" contest anyway.
- The policy lives in `AutoKill.Core` with tests and knows nothing about the game. The
  layer that reads gearsets and equips them is thin enough to read in one sitting, which
  is the only part that cannot be tested here.
