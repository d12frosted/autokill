# 0017. A run drives the rotation

## Status

Accepted

Supersedes [0007](0007-never-take-over-a-running-rotation.md).

## Context

0007 decided that a rotation already running belonged to the player: take no lease,
change no setting, leave it exactly as found. The reasoning was about courtesy, and it
held for everything it was written about. It did not hold for targeting.

Wrath picks its own target unless told not to. Which mob it picks is `DPSRotationMode`,
and the hard target override this plugin already sets only applies while there is a hard
target in range. Between finishing one mob and reaching the next there is not one:
AutoKill hands Wrath a target only once the quarry is close enough to fight, and leaves
auto-rotation on for the whole run rather than switching it on per fight. Every tick in
that gap, Wrath falls back to the mode. `Nearest`, the usual choice, is the worst
possible one here, because it prefers enemies that are not already fighting you.

What that looks like from the outside is a character walking a circuit and starting a
fight with every harmless thing it passes. It is worst on the early hunting log ranks,
which are levels 1 to 20 with no mount, so the whole circuit is walked at ground level
through everything standing on it.

Under 0007 this was unfixable for anybody whose rotation was already on, which is most
people who have Wrath at all. The needs panel could describe the problem, and that was
the whole of the remedy.

The lease is what makes the fix fair. Everything set under one is Wrath's to put back
when the lease ends, so taking a lease over a running rotation borrows settings for the
length of a run rather than editing somebody's configuration.

## Decision

Take a lease for every run, whether or not a rotation is already running, and apply the
settings the run needs under it.

Pin `DPSRotationMode` to `Manual` for the length of the run. Manual is the only mode
that never chooses a target, so nothing is attacked but what this plugin targeted.

Switch auto-rotation on only when it was off, and switch it off at the end only if this
plugin was the one that switched it on. That is the one thing the lease does not undo,
and it is the part of 0007 that was right.

Set up a job with nothing in auto-mode in every case, rather than reporting it as a wall
the player has to go and fix. It is leased, so it is given back, and it is now done for a
running rotation as well as a cold one.

Everything else 0007 decided carries over unchanged: read every result rather than
treating the sequence as fire and forget, drop a refused lease and ask for one more,
throttle the retries because this runs on a tick, ask Wrath whether a rotation is running
rather than remembering having started one, register for the cancellation callback, and
recover from every reason a lease ends except a player revoking it by hand.

## Consequences

- A run attacks what it was sent for. The circuit is walked, not fought through.
- Somebody who farms with Wrath already running now has settings changed under them for
  the length of the run. They are borrowed and given back, and the needs panel says so
  rather than promising to leave them alone.
- Auto-rotation left on by the player stays on when the run ends, so the character is
  still fighting for them afterwards, exactly as before.
- `Start` is now called on every tick of a fight rather than only when nothing was
  running, so its first branch is the common one: a lease in hand and settings applied.
  A rotation switched off by hand mid-run is switched back on, which is what taking
  control means.
- Wrath's option numbers and its `DPSRotationMode` values are now both things this plugin
  depends on. They are assigned rather than positional, so they survive the list growing,
  but they are still somebody else's contract discovered at runtime.
