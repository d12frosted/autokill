# 0009. A core library that never sees the game

## Status

Accepted

## Context

Most of what could be wrong in this plugin is not about the game at all. A map projection
with a sign error, clustering that merges two fields into one, a stop condition that ends
a run early, a circuit that always picks the same spot: none of those need a client to be
wrong, and none of them can be checked by watching a character run about.

Testing anything that touches Dalamud means a running game, which is not available on the
machine this is developed on.

## Decision

Two projects. `AutoKill.Core` targets plain `net10.0` and references nothing from Dalamud,
the game, or Lumina. Everything that needs the client lives in the plugin project.

Core holds the map projection, spot and area clustering, stop conditions, and circuit
selection. All of it is tested, and the tests run anywhere.

Where the split is awkward, the split wins. Stop conditions describe themselves with item
ids rather than names, because names would require the game; the naming happens in the
plugin where the index is.

## Consequences

- 47 tests run in about 20 milliseconds on any machine, with no game and no Dalamud.
- Rules that are easy to get subtly wrong are pinned by examples rather than by argument.
  The cap on readiness in circuit selection exists because writing its test made the flaw
  in the uncapped version obvious.
- Some ceremony: a value sometimes has to be passed in rather than looked up.
