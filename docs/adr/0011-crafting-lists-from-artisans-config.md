# 0011. Crafting lists from Artisan's config file

## Status

Accepted

## Context

Almost everything worth farming a mob for is a crafting material, and the amount worth
farming is decided by a crafting list somebody has already written. Retyping the item and
the number into a search box is work that has been done once already.

Artisan is where those lists live. GatherBuddy Reborn imports them by reflecting into
Artisan's loaded assembly: it walks Dalamud's internals to find the plugin instance, reads
`Config.NewCraftingLists` off it, and invokes `CraftingListFunctions.ListMaterials` by
name. That works, and it needs a reflection helper library, a running Artisan, and a
tolerance for private names changing.

Artisan writes the same lists to `pluginConfigs/Artisan.json`, which sits beside this
plugin's own configuration file. The fields that matter are a list id, a name, and recipe
rows with a craft count.

There is a second question underneath: what a list "needs". Artisan's material panel
counts the direct ingredients of each recipe on the list and stops there. Subcrafts are
expanded only if they are on the list too.

## Decision

Read `Artisan.json` directly. No IPC, no reflection, no dependency on Artisan being
loaded. The file is re-read when its timestamp changes, which is checked at most once a
second while the tab is open.

Follow subcrafts down rather than stopping at direct ingredients, multiplying properly by
recipe yield at each step, and stop at any item that has an entry of its own on the list,
since that entry already contributes its own ingredients.

Offer only the materials something is known to drop, and say how many were left out.

## Consequences

- Nothing here breaks when Artisan is updated, unloaded, or absent. The cost is that a
  list being edited right now reads as it was last saved.
- Following subcrafts down is what makes the feature work at all. A mob drop is never the
  item on a list: it is a hide two steps under it, tanned into leather and then sewn into
  the thing being made. Counting only direct ingredients finds nothing for most lists.
- The material totals can therefore differ from Artisan's own panel, which shows fewer
  rows. Both are correct about different questions.
- Intermediates are listed alongside what they are made of. Only one of the two is ever a
  mob drop, so nothing misleading survives the filter.
- Only the handful of fields being read can break, and a file that fails to parse is
  logged and treated as no lists rather than as an error.

## Amendment: one call out to Artisan

Reading stays as decided, and writing Artisan's file is still not on the table. There is
one call out.

Artisan saves its config on every list edit, except one. Filling a list with "Add all
visible" adds the recipes in a background task and then, in the continuation, refreshes its
own table before saving. The refresh reads the local player, which Dalamud only allows on
the main thread, so the exception lands on the line before the save. The recipes stay in
memory and never reach the file, and the list reads as empty here while being full in
Artisan. Seen on Artisan 4.0.5.18 against Dalamud 15.0.3.2.

So an empty list now says it is empty rather than "nothing a mob drops", which was the same
sentence a full list of gathered materials gets, and it carries a button that asks Artisan
to write its file. There is no endpoint for "save yourself", so the ask is
`Artisan.ChangeStandardMinimumStepsBeforeMiracle` handed back the value already in the
file: every `Change...` endpoint assigns a value and then saves the whole config, so this
saves everything and changes nothing.

It is skipped while Artisan is busy, since another plugin may have a temporary override in
flight over the same setting, and it is a button rather than something done on a timer,
because writing another plugin's configuration is not something to do behind somebody's
back.
