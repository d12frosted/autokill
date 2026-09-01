# 0007. Never take over a running rotation

## Status

Superseded by [0017](0017-a-run-drives-the-rotation.md).

## Context

Wrath Combo hands out leases and takes configuration through IPC, so a plugin can enable
auto-rotation and force it to open on targets rather than wait for a fight to start.

An early integration took a lease unconditionally and then set its options. Against a
player who already had auto-rotation running, this reached into settings they had chosen
and, worse, switched their rotation off when the run finished.

A lease is more than a handle. Everything set under one is Wrath's to put back when the
lease ends, so setting up a job that was never configured for auto-rotation is a loan
rather than an edit to somebody's configuration. That is what makes it fair to do at all.

A lease can also end without being released: the player can revoke it, Wrath can be
disabled, and changing job ends it every time. Nothing announces this except a callback
Wrath offers to make.

It was also wrong mechanically. Every set call returns a result rather than throwing, and
a refusal is ordinary: the lease can be invalid, blacklisted, or the player not ready.
Treating the whole sequence as fire and forget inside one try block turned the first
refusal into an apparent crash, and an invalid lease was retried forever because nothing
noticed it had been refused.

## Decision

If auto-rotation is already on, do nothing at all: take no lease, change no setting.

Otherwise take a lease, enable auto-rotation first and configure afterwards, read every
result, and drop an invalid lease so the next attempt registers afresh. On stop, switch
auto-rotation off only if this plugin was the one that turned it on.

Set up the current job if it has nothing enabled in auto-mode, since that is leased and
therefore given back, and tell Wrath to attack the mob this plugin targeted rather than
choosing its own.

Ask Wrath whether a rotation is running rather than remembering having started one, and
register for the cancellation callback. Recover from every reason a lease ends except one:
a player who revoked it by hand is left alone for the rest of the run.

## Consequences

- A player who farms with Wrath already running sees no change to their setup, which is
  the least surprising behaviour.
- A refused lease degrades to "no rotation backend, fighting is up to you" rather than
  looking like a failure of the farming loop.
- A job change no longer quietly ends the fighting. It ends the lease, the next tick takes
  another, and the new job is set up the same way the first one was.
- Retaking a lease is throttled and gives up quietly, because it happens on a tick that
  runs many times a second.
- Leaving a running rotation alone means it can be on while the job has nothing enabled in
  auto-mode, which fights exactly as well as no rotation plugin at all. That is reported
  in the needs panel rather than fixed silently, since fixing it would be the reaching in
  that this record exists to forbid.
- Wrath's contract is discovered at runtime and can drift. The IPC layer logs refusals with
  their result codes so a future drift is visible rather than silent.
