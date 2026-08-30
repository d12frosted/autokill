# autokill-data

Builds the index AutoKill needs: which mobs drop which items, and where those
mobs stand.

Neither fact is in the game client. Loot tables are not shipped in the Excel
sheets, and mobs are spawned by the server, so there is no spawn table to read
either. Both have to come from community datasets.

## Where the data comes from

| what | source | keyed by |
|---|---|---|
| item drop tables | [Garland Tools](https://www.garlandtools.org) | composite mob id, low 10 digits are BNpcName |
| observed spawn positions | [FFXIV Teamcraft](https://github.com/ffxiv-teamcraft/ffxiv-teamcraft) `monsters.json` | BNpcName |
| mob levels | Teamcraft `monsters.json`, per position | BNpcName |
| mob names | Teamcraft `mobs.json`, Garland | BNpcName |
| map projection, territory, expansion | [xivapi/ffxiv-datamining](https://github.com/xivapi/ffxiv-datamining) CSVs and Teamcraft `maps.json` | Map / TerritoryType |
| hunting log entries | datamining `MonsterNote` and `MonsterNoteTarget` CSVs | BNpcName |
| the other half of the shipped positions | [LuminaSupplemental](https://github.com/Critical-Impact/LuminaSupplemental) `MobSpawn.csv` | BNpcName |

The last two are only read by `hunting-log`. The plugin gets LuminaSupplemental's
half at runtime from the package rather than from here, and the hunting log
straight out of the game's sheets, but measuring what the plugin will know needs
both on this side too.

BNpcName is the join column. Garland keys a mob by `subLocationPlaceName * 10^10
+ BNpcName`, so one creature seen in several named sub-areas appears several
times and the entries have to be merged.

Teamcraft records positions as map coordinates. vnavmesh wants world
coordinates, so they are converted with the Map sheet's size factor and offsets.
Height is dropped rather than converted: map elevation is unreliable, and the
plugin snaps points onto the navmesh floor anyway.

Levels are kept per position, not per mob. One name covers creatures forty
levels apart, and a fifth of the map groups in this data hold more than one
level. Three percent of the points carry no level and 302 mobs carry none
anywhere, so zero has to keep meaning "unrecorded" all the way through.

## Usage

```sh
uv run autokill-data build         # fetch, join, write out/autokill-data.json
uv run autokill-data coverage      # how much of the game is actually covered
uv run autokill-data hunting-log   # how much of the hunting log can be farmed
uv run autokill-data lookup "Aldgoat Skin"
uv run pytest                      # tests
```

The first build fetches a drop table per Garland mob entry, which is a few
thousand small requests against a volunteer-run site. Everything is cached under
`.cache`, so it only happens once.

## What the data actually covers

Measured, not estimated:

```
mobs known at all                 2818
  farmable (have positions)       1841
  farmable and drop something      333

items dropped by a known mob       295
  reachable (mob has a spot)       190

expansion              mobs  w/ drops   spots   items
-----------------------------------------------------
A Realm Reborn          514       173    2983      90
Heavensward             232        45    1591      39
Stormblood              182        32    1550      29
Shadowbringers          230        16    1410       3
Endwalker               240        40    1077      29
Dawntrail               443        27    1896      33
```

Kill farming works everywhere: 1,841 mobs across all six expansions have
somewhere to go. Drop farming is thinner and leans towards older content, but it
is not the ARR-only feature the Garland data alone suggests.

Merging the two drop tables matters more than it looks. Garland alone knows 188
items and claims Shadowbringers and Endwalker have almost nothing; Teamcraft
adds 107 items Garland never recorded, and Endwalker goes from 8 items to 29.
Almasty Fur in Garlemald is the case that exposed this: a perfectly ordinary
Endwalker crafting material that Garland has no record of at all.

### The hunting log

The log is the one list here that the game writes itself, so the only question
is whether the position data knows where its mobs stand. `hunting-log` answers
it:

```
log                        entries  kills  mobs  placed  in zone  entries ok
----------------------------------------------------------------------------
Gladiator                       50    223    62      62       62          50
Pugilist                        50    265    68      68       68          50
Marauder                        50    226    62      62       62          50
Lancer                          50    244    67      67       67          50
Archer                          50    244    68      68       68          50
Conjurer                        50    184    59      59       59          50
Thaumaturge                     50    238    66      66       66          50
Arcanist                        50    197    59      59       59          50
Rogue                           50    166    65      65       65          50
Maelstrom                       30     92    30      29       23          23
Order of the Twin Adder         30    101    30      29       22          22
Immortal Flames                 30     89    30      29       22          22
```

Every mob on every class log stands somewhere the data records, inside the zone
the log itself names. That is 450 entries out of 450, which nothing else here
comes close to.

The 23 misses are all Grand Company entries, and all of them are the same thing:
those logs send you into Halatali, Cutter's Cry, the Sunken Temple of Qarn and
the Wanderer's Palace, and a dungeon is not somewhere a run can go.

Coverage is still partial and always will be, since all of it is crowd-sourced.
Whether GamerEscape (which Monster Loot Hunter scrapes) knows more is untested;
it sits behind Cloudflare and refuses automated requests.

## Caveats

- Position data is crowd-sourced and partial. Plenty of mobs have names and drop
  tables but nowhere recorded to find them.
- FATE-only spawns are excluded; they exist only while their FATE is up.
- Dungeon and housing maps are excluded.
- Drop tables say nothing about drop rates, so "farm until I have 30" can only
  be measured by watching the inventory, not predicted.
