# Changelog

What changed in each release, written for people using the plugin rather than
reading the diff. Dates are when the tag went out.

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
