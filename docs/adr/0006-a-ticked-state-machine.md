# 0006. A ticked state machine

## Status

Accepted

## Context

A run reads as a sequence: teleport, travel, hunt, repeat. Written as an async method that
awaits each step in turn it would be shorter and easier to follow.

Every one of those steps can be interrupted by something outside the plugin's control. A
loading screen arrives mid-journey. The character dies. vnavmesh gives up on a path. Wrath
revokes its lease. The player takes manual control, or mounts, or walks away. An awaited
sequence has to anticipate each of those at every await point, and unwind correctly when
one happens.

## Decision

One state machine, ticked from the framework update, re-reading the world every tick.

Each phase is a method that looks at the world as it currently is and decides what to do
about it right now. Nothing is remembered across ticks that can be observed instead. If
the character is suddenly in the wrong zone, the travel phase notices and hands back to
teleporting; no unwinding is required because nothing was in flight.

Actions that must not be spammed carry their own cooldowns rather than being sequenced:
mounting, jumping, teleporting, re-pathing.

## Consequences

- Interruptions are handled by construction rather than by anticipation. Being dismounted
  by hand mid-journey costs one tick.
- The machine is a state at any instant, which is what makes the run window and the trace
  file possible: both just describe the current state.
- Reading it means reading conditions rather than a sequence, which is less obvious. The
  phase enum and the status string are there to make the current state legible.
