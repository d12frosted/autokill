# AutoKill

A Dalamud plugin for grinding a specific mob, or a specific drop, until you have enough
of it. Pick a mob by name or pick an item you want, and AutoKill works out where to farm
it, travels there and grinds until the target is met.

Movement is vnavmesh, travel is Lifestream plus the aetheryte teleport, and the fighting
is delegated to whichever rotation backend you already use (Wrath Combo, Rotation Solver
Reborn or BossMod Reborn).

This is early. Right now `/autokill` opens a window that answers the lookup half of the
problem: search a mob by name, or search for an item and see which mobs drop it, where
they stand and how thickly they spawn there. Nothing moves your character yet.

## Layout

| project | what it is |
|---|---|
| `AutoKill` | the Dalamud plugin: data loading, UI, and eventually the IPC and farming loop |
| `AutoKill.Core` | logic that needs no game client, so it can be tested on any platform |
| `AutoKill.Tests` | tests for `AutoKill.Core` |
| `tools/data` | a standalone extractor, kept as a supplement to the shipped dataset |

## Automation risk

This drives your character unattended in the open world. That is against the game's terms
of service and people do lose accounts for it. Your call.

## Building

Requires the .NET 10 SDK and a Dalamud dev install. The build finds Dalamud automatically
on Windows (XIVLauncher), macOS (XIV on Mac) and Linux (XIVLauncher.Core); set
`DALAMUD_HOME` to override.

```sh
dotnet build AutoKill/AutoKill.csproj -c Release
```

Building from macOS or Linux works because the project sets `EnableWindowsTargeting`.

To load the result in game, add the output directory as a dev plugin location in
Dalamud's settings (Experimental tab). On macOS the game runs under Wine, where `/` is
mounted as `Z:`, so the path to enter is the Windows-shaped one:

```
Z:\Users\<you>\Developer\autokill\AutoKill\bin\Release
```

## Data

The plugin needs to know which mobs drop which items, and where those mobs stand. Neither
lives in the game client, so both are assembled from community datasets by the extractor
in `tools/data`. See [tools/data/README.md](tools/data/README.md).
