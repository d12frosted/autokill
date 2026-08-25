# The ledger style

AutoKill wears the family signature, the ledger style, first written for Tataru.
`AutoKill/UI/Style.cs` enforces this guide; this file explains it. When the two
disagree, fix one of them in the same change. The canonical account lives with
Tataru (`docs/style.md` there); this copy adapts the examples to this plugin.

## The creed

A ledger is read in one pass or it is not read at all.

- Names on the left, states and numbers flush right where they form a column.
- One accent, for what still wants doing, used for nothing else.
- Everything finished is quieter than everything open.
- Each fact is said once, in the place that can act on it.
- Detail that is only sometimes wanted lives in a tooltip, not on the line.
- A bar stands in for any pair of numbers somebody would otherwise have to divide.
- The one loud control is the one that moves the character.

## The palette

Every colour in the window is one of these tokens. No literals at call sites; the
only exception is data-derived colour (item icons).

| Token    | Meaning                                                       |
| -------- | ------------------------------------------------------------- |
| `Accent` | What still wants doing. Used for nothing else.                 |
| `Brand`  | The accent worn thin. Masthead only: the feather and its rule. |
| `Plain`  | A row's own words.                                             |
| `Muted`  | Everything that supports the accent: leads, states at rest.    |
| `Good`   | Finished, met, ready.                                          |
| `Warn`   | Needs a person's attention before the plan works.              |
| `Bad`    | Broken. Rare by design.                                        |
| `Paper`  | The window's ground: warm near-black.                          |
| `Veil`   | The faintest wash of light: empty cells, idle chrome.          |
| `Rule`   | An edge or a divider: barely more present than the veil.       |

`Accent` discipline is the heart of the guide: if accent appears anywhere that is
not an open, actionable want, the signature is already eroding. `Brand` exists
precisely so the masthead does not spend the accent. In this window the browse
screens spend the accent on offered ground (`Style.Place`): on a screen whose whole
business is choosing where to go, the choice is the thing that wants doing. A run
already finished, or a name in History, never wears it.

## The shell

The window wears its own chrome - paper, steel trim on the title bar and active
tab, veiled frames and scrollbars - pushed by `Style.Shell()` around the whole
frame (`PreDraw`/`PostDraw`, not inside `Draw`), so the window looks the same on
every install regardless of the user's Dalamud theme. The palette assumes dark
paper; owning the background is what makes that assumption safe.

The trim is this plugin's own metal: Tataru wears bronze, and AutoKill's business
is the blade, so its trim is a colder steel in the same quiet register. Everything
else in the shell is family.

Every window of ours opens with `Style.Masthead(name, context)`: the feather in
brand, the plugin's name in plain, the context (the logged-in character) against
the right edge, and a thin brand rule beneath. The masthead is the anchor every
screen hangs from - on the overlay, which has no title bar, it is also the only
place the plugin's name appears at all.

## Composition

- A row is a sentence: name and level on the left, quiet actions riding along,
  verdict or count trailing right (`Style.Trailing`). Example: `Kokkine Petalouda
  Lv82  [repeat]                    34 killed in 00:12:40   161/h`.
- Detail sits one indent under its line, never deeper than two levels. Sections
  of one block sit a small `Gap` apart.
- Say each fact once. A summary yields to detail that is on screen.
- Empty states go through `Style.Nothing`: one quiet sentence with air around it,
  naming the way forward when there is one. Example: `No hunt bills in hand. Pick
  some up from a hunt board and they turn up here.`

## Words

- States, verdicts and everyday actions are lowercase fragments: `done`, `paused`,
  `repeat`, `back to the run`, `3 to farm`, `nowhere known`.
- Proper names keep their capitals; in-game names that read ambiguously in a
  sentence wear quotes: `only while the FATE "The Winged" is running`.
- Counts read `3 of 12` or `3 / 12`, joined facts read `a · b · c`.
- Tooltips (`Style.Explain`) are full sentences with capitals and periods; they
  carry the why, the line carries the what.
- Headings (`Style.Heading`) are lowercase at the call site; the style uppercases
  them. A heading names a section of the window, never a form field.

## Controls

Four tiers, quietest first:

1. `Style.TrailingRemove` - the destructive x: nearly invisible, right edge, as far
   from content as the row allows (forget this run).
2. `Style.Quiet` - everyday row actions and ways of merely looking elsewhere
   (`repeat`, `back`, `map`, `browse`, `adjust targets`): reads as text until
   hovered.
3. `Style.Row` - a small real button for actions that move machinery but stay on
   the row (`pause`, `resume`, `stop`, `pick it back up`).
4. `Style.Commit` - the one way a form says "do it" (`start`, `apply`, `farm the
   whole list`): full height to stand level with inputs, accent word.

Checkboxes are for durable preferences (Settings) and the plan's own knobs;
everything the game can answer is watched, not ticked.

This plugin adds two full-width pickable rows on top of the tiers: `Style.Pick`
(a row with a mark saying what picking it does - the mark turns accent under the
mouse) and `Style.Named` (a mob row with its level beside the name). Both make the
whole width answer to the mouse, which is why `Trailing` here measures from the
window's own right edge rather than from where the last thing ended.

## Icons

Two roles, never mixed:

- A mark on a pickable row names what picking does: `Khanda` go and kill (the
  default), `Search` search instead, `Dice` whatever suits the field, `ListUl` a
  crafting list. `Feather` is the masthead's and nothing else's.
- A verdict mark rides trailing text only when the state is worth seeing before
  the words: `Check` enough gathered.

`Cog` on the overlay is the one picture used as a control, because that row has no
room for a word. Extend the table here before using a new mark.

Item icons (`Icons.Draw`) only ever sit directly before an item's name, sized from
the line they belong to.

## Sizes

Every fixed length goes through `Style.Px`, which scales design pixels by the
user's global scale. Font-derived sizes (`GetFontSize`, `GetTextLineHeight`) are
already scaled and stay as they are, and `WindowSizeConstraints` are scaled by
Dalamud itself. No bare pixel literal reaches ImGui.

## Progress

`Style.Progress` is the only bar: accent while underway, quiet green when full
(a finished goal is a fact, not a call to action), veil for the track, thin. The
labelled overload draws the goal's name, its numbers trailing right, and the bar
beneath - the shape every watched goal in the run screen and the overlay shares.

## Enforcement

- New drawing goes through `Style` helpers. Raw `ImGui.TextColored` is fine when
  the colour is a state ternary over tokens; raw colour literals are not.
- Raw `ImGui.Button`/`SmallButton` never appears in a screen; one of the four
  tiers does.
- `ImGui.Separator` is acceptable (the shell colours it to `Rule`), but ask
  whether a `Gap` says it more quietly.
- Before shipping a surface, read it against the creed: is anything said twice, is
  the accent spent on something that is not a want, does the eye travel further
  than it has to?

## Porting to a sibling plugin

Copy `Style.cs` whole, keep the tokens and their meanings, change only the trim if
the sibling wants its own metal - that is exactly how this plugin got its steel.
Reuse the masthead with the sibling's name and the same feather - the feather is
the family mark, the name is the individual. Bring this file along and edit its
examples; the creed does not change.
