# Changelog

What changed in each release, written for people using the plugin rather than
reading the diff. Dates are when the tag went out.

## Unreleased

### Added

- **The hunting log.** A new Log tab, showing what every class and Grand
  Company log still owes: the rank you are on, the ten entries in it, the mobs
  each wants and how many are in. The counts are the game's own, so a rank
  picks up wherever you left it. "Farm rank 2" goes and does the whole thing:
  the entries are grouped into the fewest zones that hold them, one stop per
  field, and each stop goes after every mob of that rank standing in it rather
  than one at a time. A rank costs about five teleports that way instead of
  ten. Single entries can be farmed on their own too.

  Because a log only counts the kill for the class it belongs to, a run from
  here puts that class on rather than picking whatever clears the field
  fastest. Logs you cannot farm say which wall it is, dimmed and sorted below
  the rest: a class you have never picked up reads "not unlocked", and one with
  no gearset reads "no gearset", because a bare class cannot be equipped. The
  Grand Company log is there too, the one you belong to, and its dungeon
  entries say "nowhere recorded in that zone", which is the truth: a run cannot
  go into Halatali.

  How far above the class's own level it will reach is a setting, three levels
  by default. The log is ordered by level and the class levels while it runs,
  so what is offered opens up as it goes.

  A run counting mobs separately goes after exactly the ones it still has a
  count for. A field carrying five entries does not fill them up together, and
  the two that come back fastest would otherwise keep the ground looking busy,
  keep the circuit from ever judging a spot cleared, and leave the run standing
  in one place killing what it already had enough of. The same rule catches a
  mob a stop earlier in the list already finished, and one dropped from what a
  run picked back up after a death still owes: either way it stands in the
  field with no number behind it, and killing it would never end.

- **Picking a run back up goes as the same class.** A run that dies is offered
  again with what it still owed, and it used to come back as whatever the job
  policy picked. A hunting log counts the kill for one class and nobody else,
  so it now comes back as that one. It is also no longer offered at all when
  everything asked for was reached and dying is merely what ended it, since
  starting again on that is a run with no number to reach.

- **Pause.** A running farm can be paused and resumed from the window. Paused,
  it lets go of everything: the route, the rotation, the target it was chasing.
  The clock a time limit runs against stops with it, so a run paused for ten
  minutes still farms for the minutes you asked for. Its eyes stay open, though:
  kills it already started, items reaching your bags, and respawns it can still
  vouch for all keep counting, so a pause does not put a hole in what it learns
  or what it owes you. Item and kill goals still end the run even while paused,
  because reached is reached.

- **It pauses itself when you take the controls.** Walking the character away
  mid-run used to mean fighting the plugin for your own character. Movement the
  run did not ask for now pauses it, with a chat line saying so, and it waits
  for you to press Resume. It watches ground covered rather than keys pressed,
  so a gamepad counts and rebound keys do not confuse it. Combat is left alone:
  knockbacks move a character with nobody driving, so repositioning mid-fight
  is not read as a takeover. Grabbing the controls in a fight is what the Pause
  button is for.

- **It pauses instead of fighting a teleport or a duty.** Ending up in another
  zone mid-run, whether a duty took you or you teleported yourself, used to
  make the run immediately try to teleport back to the field. Now it pauses and
  says why. Resume when you are done and it makes its own way back.

- **It can bring you home.** Tick "teleport back when it ends" on the plan and
  a run that ends on its own teleports you back, the way Gatherbuddy Reborn
  does it: either to an aetheryte in the zone you set off from, or to your home
  point, whichever you pick beside the checkbox. It waits out the combat a last
  kill leaves trailing before casting, and if the way stays blocked for half a
  minute it stays put rather than yanking you somewhere as a surprise. A run
  you stop yourself never teleports: you are standing right there. The choice
  sticks as the default for the next run, and lives in Settings too.

- **The plan can show itself on the map.** A "map" button beside the
  coordinates flags the chosen area and opens the game map on it, so where you
  are about to send yourself is a picture rather than a pair of numbers taken
  on trust. The flag is the same one the run sets when it travels.

- **The stop line can be moved mid-run.** "Actually fifty, not thirty" used to
  cost a stop and a whole replan. The run screen now has "adjust targets": the
  same controls as the plan, prefilled with what the run is aiming at, applied
  without stopping. The target itself stays fixed; the stop line is yours to
  move. A line moved behind where the run already stands ends it on the next
  tick, which is what asking for less than you have means.

- **A sound when a run ends, if you want one.** The chat line and the toast are
  silent, and a run ends precisely when you are looking at something else. Pick
  one of the game's sixteen chat sounds in Settings, the same ones `<se.1>`
  through `<se.16>` play, with a button to hear it before committing. Off by
  default.

- **History says how fast, not just how much.** Each run now shows kills and
  items per hour next to the totals, so "which field is better for this" is
  answerable from your own runs instead of by feel. Runs under a minute show no
  rate: one kill in twenty seconds reads as 180 an hour, and a number that
  wrong is worse than none. Paused time never counts, so the rates are about
  the farming.

- **Dying does not cost the setup.** A run ended by a death offers to pick
  itself back up: same target, and only what is left of the goals, since kills
  made, items held and time spent all survive the trip to the respawn point.
  One click and it goes back to the field. Dying still always ends the run
  first, so nothing walks a fresh corpse back without being asked.

- **The run says how much longer.** Progress bars with a target now carry a
  rough time to go: "12 / 30   ~14 min". Said roughly on purpose, because
  hh:mm:ss promises more than an estimate knows.

  What this ground has given before and what it is giving now are weighed
  against each other rather than one replacing the other, so the number is
  there from the first second and does not lurch about while the run is young.
  A run two minutes old has a rate that doubles or halves on a single drop, and
  an estimate that moves like that is not worth reading. A field that really is
  poorer today is still believed once it has spent a while proving it, and a
  pace measured over one short trip gives way faster than one measured over
  hours. With no history and no pace of its own, a bar says nothing rather than
  making a number up.

- **The plan says what this ground gave last time.** Planning a field you have
  farmed before shows the most recent run over it: kills, items and how long it
  took. Which is the one honest basis for deciding whether it is worth going
  back.

- **A small progress window while it runs.** The main window is usually behind
  the game exactly when a run is going, so the run shows in a compact overlay
  instead: the mob, the status, the bars with item icons and time to go, Pause
  and Stop, all sized like the buttons everywhere else. The main window makes
  way for it as the run starts rather than
  saying the same thing twice, and the cog on the overlay opens it again at the
  tabs. When the run ends the overlay goes and the main window comes back with
  the result, which is something to read rather than watch. Turn the overlay off
  in Settings and the main window carries the run as before.

- **Browse while it runs.** The main window used to be locked to the run for
  its whole length, but reading By drop during a twenty minute grind is how the
  next run gets planned. A "browse" button on the run screen frees the window,
  one line at the top says what is still going and leads back to it, and a
  finish always brings the result forward. The one thing that stays locked is
  the running goal, which is what the lock was ever for; adjust targets is the
  door for that. Starting a second run while one is going is refused rather
  than quietly replacing it, which was fine when the plan could not be reached
  mid-run and would be a trap now.

- **Farm the whole list.** The crafting list view gets one button that farms
  every outstanding material in turn: one run per material, stops in the same
  zone kept together, each going for exactly what is still missing when its
  turn comes, so whatever earlier stops looted is already counted. The run
  screen and the overlay say how many stops are left. Dying, full bags or
  stopping by hand ends the whole list, since every stop after would fail the
  same way; a stop whose material turns out to be covered is skipped rather
  than farmed for nothing. Materials with no recorded field are named and left
  behind rather than silently missing.

- **What a goal is likely to cost, before you set it.** Planning a field you
  have farmed before now says roughly how long the kills and items you just
  asked for should take, pooled from every past run over that ground. So "is
  this worth twenty minutes" is answerable while deciding rather than twenty
  minutes later.

### Changed

- **The window wears the family signature.** AutoKill now dresses like its
  sibling Tataru: its own dark paper and chrome on every install, whatever theme
  the rest of Dalamud wears, with a steel trim where Tataru wears bronze. Every
  window opens with the masthead (the feather, the name, your character against
  the right edge), buttons come in four deliberate tiers from a near-invisible
  x up to the one accent-worded button that moves your character, and states
  and everyday actions read as quiet lowercase fragments. The rules are written
  down in docs/style.md.

- **A run lets go of the fight it ends in.** Goals are usually met by loot from
  the mob before the one currently swinging, and handing the rotation back at
  that moment left the character standing in a fight doing nothing, which is a
  good way to die to a mob you already beat. A finished run now keeps the
  rotation until the fighting is over, then releases it. Travelling stops
  immediately either way.

- **Done lands somewhere useful.** Finishing a run picked off a crafting list
  used to drop you back on that material's field list, which is the one screen
  the list never wants next. Done now lands on the list itself, counts fresh,
  with the next thing to farm in view.

## 0.0.4 - 2026-08-20

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

- **It checks you can actually fight the thing first.** Starting a farm on a
  crafter used to work perfectly: teleport, mount, fly to the field, walk up to a
  mob and stand there, because there is no rotation to run on a Weaver. Starting
  one twenty levels short of the field worked too, right up to dying in it. In
  both cases nothing said why.

  Before anything else happens, the run now compares you against the level of
  what you picked. By default it changes job for you and goes. If you would
  rather it did not touch your job, Settings has the other two answers: refuse to
  start and say why, or say what is wrong and go anyway. Mobs a few levels up are
  killable and that last one is there for people who know it.

  Left to itself it picks something that kills things over something that
  survives them, so a damage job goes before a tank and a tank before a healer,
  and the highest of those. If you would rather it always reached for one job,
  pick it in Settings and it will, whenever that job is up to the field. Only
  jobs you have a gearset for are offered, since the game cannot put you in a job
  with no gear.

  It compares against the top of the field's range, since a field is patrolled
  whole. Where no level was ever recorded it says nothing and starts, because
  that is a gap in the data rather than a fact about the mob. A crafter is
  refused either way; that half of the check needs no data.

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
  it never measured at all, and a wide field never came back inside the ten
  minutes past which a gap is thrown away as untrustworthy. Hundreds of kills
  in either left the Learned tab still saying "not yet", and it said it as
  "0/3", as though three were on the way.

  A run now watches two more things. The spot it is standing on: it cleared it,
  it went quiet, something is standing there again. And, more useful in a busy
  field, single spawn points: one thing went down here, and later something else
  was standing here. That second one does not need the ground around it to be
  quiet, which matters because a field worth farming is never quiet and never
  left, so both of the older measurements sat there with nothing to close. It
  now records one every time anything comes back, rather than once a circuit.

  The timing runs from where a mob was first seen rather than from where it
  fell. Pulling one walks it in to wherever you are standing, sometimes tens of
  yalms from home, and the ground it dies on is nobody's spawn point.

  Anything measured across a stretch where the field was not being watched is
  thrown away instead of guessed at: something vanishing at a distance is a
  draw distance rather than a death, a spawn point left behind comes back where
  nobody is looking, and a loading screen can hide any amount of time.

  The Learned tab now says which kind of number it is showing. A timed spawn
  point is a respawn; a return trip is a respawn plus the trip, so it reads
  long. The estimate prefers timings once it has three of them, and falls back
  to return trips before falling back to a flat ninety seconds.

- **Sightings were only recorded while a run was being written to a log.** They
  sat behind the same check as the debug recorder, which is off unless you turn
  it on, so a character that never recorded a run had never collected any. What
  is learnt outlives the run, so it no longer depends on debugging one.

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
