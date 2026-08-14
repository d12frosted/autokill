<img src="assets/icon.svg" alt="" width="128" align="right">

# AutoKill

Pick a mob, or pick something you want it to drop, and AutoKill goes and farms it. It
works out where the mob lives, teleports to the zone, mounts, flies over, kills its way
around the field and stops when you have what you asked for.

Movement is [vnavmesh](https://github.com/awgil/ffxiv_navmesh). Fighting is whichever
rotation plugin you already use. AutoKill decides where to go and what to attack, and
lets those do what they are good at.

## Automation risk

This drives your character unattended in the open world. That is against the game's terms
of service and people do lose accounts for it. Your call.

## What it does

**Find something to farm.** Search by mob name, or search for an item and see which mobs
drop it, with icons, zones and how thickly each place spawns. Mobs that drop something but
have nowhere recorded to find them are still listed, and say so, rather than silently
missing.

**Farm an area, not a spot.** Mobs of one kind are spread over a field in several loose
knots. AutoKill treats the whole field as one place, flies a circuit around it, and moves
on when a knot is cleared instead of standing over a respawn timer.

**Stop when you say.** Set any mix of a kill count, a time limit, and an amount of any
item the mob drops, and choose whether the first target ends the run or all of them must
be met. Dying or filling your bags always ends it.

**Tell you it finished.** A chat line and a toast when the run ends, with the items as
proper links you can hover. The chat line is local only, like `/echo`, and it is still
there when you come back.

**Learn as it goes.** How quickly a place repopulates is not in any dataset, so AutoKill
measures it and uses it to decide when a spot is worth returning to. The second run over
the same ground routes better than the first.

**Remember what you ran.** Finished runs are kept with what you asked for, so repeating
one is a single click rather than setting it up again.

## Using it

`/autokill` opens the window. It shows one thing at a time:

- **Browsing** — four tabs: By mob, By drop, History, Learned, plus Settings.
- **Planning** — the area you chose, and what should end the run. Nothing starts until
  you press Start.
- **Running** — where it is, what it is doing, and how far along. One Stop button.
- **Finished** — what happened, until you dismiss it. Or farm the same area again.

### Settings

| setting | what it does |
|---|---|
| mount and fly beyond | how far a destination has to be before it is worth mounting. Lower means mounting for almost every hop; higher keeps it on foot inside an area |
| wait at an empty spot | how long to give a cleared knot before moving on round the circuit |
| announce starts and finishes | the chat line and toast |
| record runs to a trace file | writes what each run did, for working out afterwards where the time went |

### Learned

What farming has taught it, per mob and zone: kills, and how quickly the place comes back.
It says plainly when it has too few observations to trust rather than showing a number
built from one or two. Entries can be forgotten one at a time or all at once, which is
what you want after a zone is reworked in a patch.

## What you need

- **vnavmesh** for movement. Nothing works without it, and the window says so.
- **A rotation plugin** for fighting. Wrath Combo is wired up. Without one the loop still
  travels and targets, and leaves the fighting to you.
- An attuned aetheryte in the zone you want to farm.

If Wrath's auto-rotation is already running, AutoKill leaves it completely alone rather
than reaching into your settings.

## Coverage

Mob positions and drop tables are not in the game's files, so both come from community
data. Merged, that is **9,753 mobs** with somewhere to go and **307 items** with a mob
that can be reached. It is partial and always will be. Drop coverage leans towards older
content, but every expansion has something.

You do not need the drop data to farm an item: item counts are read from your inventory,
so "until I have 30 of these" works for anything at all, as long as you pick the mob.

## Installing

Requires the .NET 10 SDK and a Dalamud dev install.

```sh
./scripts/install.sh              # build and install
./scripts/install.sh --status     # what is built, what is registered
./scripts/install.sh --uninstall  # remove it again
```

After the first install you can run it again with the game still open: it copies the new
build across and Dalamud reloads the plugin in place.

The build finds Dalamud automatically on Windows, macOS and Linux; set `DALAMUD_HOME` to
override. Building from macOS or Linux works because the project targets Windows
explicitly.

## Layout

| project | what it is |
|---|---|
| `AutoKill` | the plugin: data, UI, IPC, and the farming loop |
| `AutoKill.Core` | logic that never sees the game, so it can be tested anywhere |
| `AutoKill.Tests` | tests for `AutoKill.Core` |
| `tools/data` | generates the spawn position data embedded in the plugin |

```sh
dotnet test AutoKill.Tests/AutoKill.Tests.csproj
```

## Why things work the way they do

See [docs/adr](docs/adr) for the decisions and their reasons, including where the data
comes from, why spawn positions carry no height, and why a run is a state machine rather
than a sequence.
