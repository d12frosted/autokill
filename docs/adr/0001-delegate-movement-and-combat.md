# 0001. Delegate movement and combat

## Status

Accepted

## Context

A farming run has to walk across a zone avoiding terrain, fly where flight is allowed,
and fight whatever it finds with a competent rotation for the current job. All three are
large problems. All three are already solved by plugins most players who would want
AutoKill already have installed: vnavmesh for navigation, Wrath Combo (or Rotation Solver,
or BossMod) for combat, and the game's own teleport for long distance travel.

Reimplementing any of them would mean maintaining a pathfinder against every zone
revision, and a rotation against every job change in every patch.

## Decision

Own the decisions, delegate the execution.

AutoKill decides *where* to go and *what* to attack. It asks vnavmesh to do the moving
and a rotation plugin to do the fighting, through their published IPC.

Every IPC call is guarded and has a fallback. A missing or disabled dependency degrades
the run rather than throwing: without vnavmesh nothing can move and the window says so;
without a rotation the loop still travels and targets, and says the fighting is up to you.

## Consequences

- AutoKill inherits improvements to navigation and rotations for free, and inherits their
  bugs too. A mid-flight landing is vnavmesh's behaviour, not something fixable here.
- The plugin is small enough to reason about. The farming loop is one state machine.
- Dependencies are discovered at runtime, not compile time, so their contracts can drift
  without warning. This has already happened once with Wrath (see 0007).
