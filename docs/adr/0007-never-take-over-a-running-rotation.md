# 0007. Never take over a running rotation

## Status

Accepted

## Context

Wrath Combo hands out leases and takes configuration through IPC, so a plugin can enable
auto-rotation and force it to open on targets rather than wait for a fight to start.

An early integration took a lease unconditionally and then set its options. Against a
player who already had auto-rotation running, this reached into settings they had chosen
and, worse, switched their rotation off when the run finished.

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

## Consequences

- A player who farms with Wrath already running sees no change to their setup, which is
  the least surprising behaviour.
- A refused lease degrades to "no rotation backend, fighting is up to you" rather than
  looking like a failure of the farming loop.
- Wrath's contract is discovered at runtime and can drift. The IPC layer logs refusals with
  their result codes so a future drift is visible rather than silent.
