# Changelog

What changed in each release, written for people using the plugin rather than
reading the diff. Dates are when the tag went out.

## Unreleased

### Added

- **By drop offers the field, not the species.** Several mobs often drop the same
  item in the same place: three kinds of petalouda share two fields in Elpis, and
  all three drop petalouda scales. Picking one of them meant flying past the other
  two and then waiting on a respawn timer with the field still full.

  The By drop tab now lists places rather than mobs. Each one names everything
  standing there that drops what you asked for, and choosing it farms all of them,
  so the kill count, the item goal and the circuit all count the whole field. If
  you want only one kind, it is still offered under each field.

  A run that goes after several kinds still learns them separately, so what it
  knows about one mob is not muddled by what it learnt about another sharing its
  ground.

- **A Hunts tab.** The hunt bills you are carrying, read out of the client, with
  each unfinished target offered in the zone the bill names and the kill goal
  set to what is left rather than the whole bill. The counts are the game's own,
  so they are right about kills from before you opened the window.

  Targets that only exist inside a FATE say so, and only offer to go while that
  FATE is actually running. There are 96 of them, all on ordinary bills, and
  standing where one would be is how a run waits forever. When the FATE is up it
  goes to where the FATE is rather than where the mob was once recorded.

  The weekly elite bill is included. Every one of those names a B rank, which is
  a mob one player is expected to kill, and marks are the best covered thing in
  the position data: 74 of the 77 stand somewhere the plugin knows about. If the
  mark is not up, the run patrols its spawn points until it is, which is what
  hunting one looks like by hand.

### Fixed

- **Wrath keeps fighting after a job change.** Changing job ends Wrath's lease, and the
  plugin used to believe its own record of having started a rotation, so the rest of the
  run was spent watching a mob that nothing was hitting. It now asks Wrath, takes the
  lease again, and sets up the new job. A player who takes control back by hand is left
  alone rather than argued with.
- **Wrath attacks the mob AutoKill picked** rather than choosing its own, which could pull
  something the run was not walking towards.
- The needs panel says when auto-rotation is on but the current job has nothing enabled in
  auto-mode. That fights exactly as well as having no rotation plugin at all, and used to
  look like the plugin doing nothing.

## 0.0.3 - 2026-08-14

### Added

- **By drop can start from a crafting list.** If Artisan is installed, the By
  drop tab offers your crafting lists instead of the search box. Pick one and it
  shows the materials a mob can supply, how many are in your bags and how many
  are still to find. Choosing one carries the amount through, so the goal is set
  as well as the target.

  Subcrafts are followed down, which is the point: nothing a mob drops is ever
  the item on the list, it is a hide two steps under it. Materials nothing drops
  are left out, but counted, so a list showing two rows out of thirty does not
  read as broken.

  Crystals are left out too. Mobs drop them and every list wants hundreds, so
  they would sort straight to the top and bury the row worth farming.

  Lists are read from Artisan's own config file, so this keeps working with
  Artisan unloaded, and picks up edits once Artisan saves them.

## 0.0.2 - 2026-08-14

### Fixed

- **Flying works.** A route begun on foot stayed a ground route after mounting,
  so it rode the whole way there. The route is now re-issued when the way it is
  travelling changes.
- **Arrival is judged by what your job can attack from.** A caster routed to
  20 yalms would arrive and still think itself too far to start, and hover there.
- **Dismounting takes a moment rather than two minutes a run.** It presses what
  the mount actually expects, twice, which is what it takes from the air.

### Added

- **Goes for mobs it can see** rather than the point it was heading for, and
  stays mounted between kills.
- **Chocobo companion**, kept out and in the stance you choose. It fights, and it
  earns its own experience while it is there.
- **A needs panel in settings** saying whether vnavmesh and Wrath Combo are
  installed, enabled and answering. Missing ones used to produce nothing but a
  character standing still.

### Changed

- The two travel settings say what they actually do, and their defaults were
  retuned now that attack range is taken off the distance.

## 0.0.1 - 2026-08-14

First release.

- Search by mob, or by something you want it to drop, from community spawn and
  drop data merged into the plugin.
- Farm a whole area rather than one spot, flying a circuit around it.
- Stop on any mix of kills, elapsed time and amounts of an item, first target or
  all of them. Dying or filling your bags always ends it.
- Movement through vnavmesh, fighting through Wrath Combo, and Wrath left alone
  if its auto-rotation is already on.
- Learns how quickly a place repopulates and uses it to decide where to go next.
- Run history, so a run can be repeated in one click.
- A chat line and toast when a run ends, with items as links you can hover.
- Optional trace file per run, for working out where the time went.
