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

- **Levels, beside the names they belong to.** Every mob, field and place in the
  window now says what level the things standing there are, so a target can be
  ruled out before flying to it rather than after. Somewhere holding mobs of
  more than one level says the span, which is a fifth of the recorded places.

  A mob's row carries the whole span it is found at, since one name often covers
  a creature in a starting zone and the same creature forty levels later. Each
  place under it says which of those that ground actually is.

  Levels come from the position data, so the 302 mobs whose positions came from
  the other source have none. Those say nothing rather than reading "Lv0".

### Changed

- **The window was reworked.** Every screen now puts names on the left and
  numbers flush right, so counts form a column you can compare down the page
  instead of reading one row at a time. Rows that can be picked say what
  picking them does and carry a blade to show they are live, which retired the
  buttons that sat next to a label repeating the word "choose".

  Mob names are capitalised. The sheets keep them in lower case, which is fine
  in a target bar and unreadable in a list of twenty. Mobs sharing a field are
  named by what tells them apart, with the ending they share said once, so
  "petalouda" is no longer written three times on one line.

  A crafting list now shows what is already gathered dimmed and marked done,
  and what is still owed at full strength with the count to go, so the list can
  be answered by looking rather than by subtracting.

  A run draws its kill, time and item goals as bars rather than as two numbers
  with a slash.

### Fixed

- **A refused dismount no longer holds the run in the air.** The game will not
  dismount everywhere: over water, straddling a fence, hovering where the drop
  is not a landing. The run used to answer that by pressing the button again
  every half second, forever, with the status stuck on "dismounting". Worse, a
  mob pacing underneath kept resetting the clock that would otherwise have
  called it stuck, so it did not even give up.

  When the presses have got nowhere for five seconds it now rides down to the
  target's own feet and gets off there. The target is standing on that ground,
  so that ground can be stood on. If the spot it lands at still refuses, it
  waits out the same patience and tries again from there.

- **A mob it cannot get to no longer ends the run.** Targets are picked by
  straight line distance, which says nothing about whether there is a way there.
  Across a gorge, up a cliff, on a rock the navmesh does not cover, or simply
  behind something solid, the run would head for one forever: the route ends
  short of it, picking again picks the same one, and because something was
  always in sight the circuit never decided the spot was done and moved on. A
  full field and hours on the clock, spent on one mob.

  It now looks before it stands. Once within reach it checks there is a clear
  line to the target from where it would be standing, which in the air is the
  ground underneath rather than the saddle, since that is where the dismount
  puts it. Nothing in the way, and it stops there. Something in the way, a rock
  or the lip of a ledge with the target below it, and it keeps going, and stops
  at the first place the target can be seen from, which is usually a few yalms
  round the rock and still at range. A ranged job stays a ranged job; it does
  not march into melee because a boulder was in the way, and it does not land
  on a ledge, wait, and find out the hard way.

  Where looking says nothing, it watches whether anything is actually happening.
  Either the distance is coming down or the target's health is, and if neither
  has for ten seconds it walks in to melee, the one place a target can always
  be seen from, which re-routes around whatever is in the way. If that changes
  nothing either, it leaves that one alone for a while and gets on with the rest
  of the field. One that wanders off from wherever it was abandoned is worth
  trying again at once.

  The watching makes no attempt to tell the reasons apart. No path, no line of
  sight, a snag on the scenery and a rotation sitting idle all look identical
  from the outside, and none of them is worth standing still for.

- **A spot it cannot get to no longer ends the run either.** Spawn positions
  carry no height, so a spot is a pair of coordinates dropped onto whatever the
  navmesh says is underneath. Sometimes there is nothing sensible underneath:
  the point lands inside a cliff, on a ledge with no way up, or out over water.
  The route then ends short and the run hangs in the air next to it, or is
  refused and the run asks again every two seconds until you notice.

  A journey that has not got any closer for fifteen seconds now asks the mesh
  where the spot is all over again and takes a fresh route to it, which is
  enough when the first answer was worked out while the zone was still loading.
  If that gets no further either, the spot is written off and the circuit goes
  somewhere else, coming back to it only after five minutes in case the trouble
  was the approach rather than the place.

  When there is nowhere left to go the run stops and says so, naming how many
  spots it could not reach, rather than flying at a cliff until the time limit
  runs out.

- **Kills are counted from the fight, not from the choice.** A mob picked out
  and then never reached was still on the books, so when anything else killed it
  the run scored it and counted it towards the goal. It is only counted now once
  the run is in range and swinging at it.

- **The places farmed hardest now learn something.** How long a spot takes to
  come back was only ever measured on the return trip: leave a spot, come back,
  see how long it had been. A mob with a single recorded spot never leaves, so
  it never measured at all, and a field of thirty spots never came back inside
  the ten minutes past which a gap is thrown away as untrustworthy. Hundreds of
  kills in either could leave the Learned tab still saying "not yet".

  A run now also watches the spot it is standing on: it cleared it, it went
  quiet, something is standing there again. That number has no travel in it, so
  it is closer to a respawn time than anything recorded before, and it turns up
  exactly where nothing used to.

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
