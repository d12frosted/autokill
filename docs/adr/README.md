# Architecture decision records

Numbered records of decisions that shape AutoKill. Format is Nygard-style: context,
decision, consequences. One file per decision, never renumbered, never deleted. If a
decision is reversed, add a new ADR that supersedes the old one and mark the old one
`Superseded by NNNN`.

Status values: `Proposed`, `Accepted`, `Superseded by NNNN`, `Rejected`.

| # | Title | Status |
|---|-------|--------|
| [0001](0001-delegate-movement-and-combat.md) | Delegate movement and combat | Accepted |
| [0002](0002-where-mob-data-comes-from.md) | Where mob data comes from | Accepted |
| [0003](0003-spawn-data-has-no-height.md) | Spawn data has no height | Accepted |
| [0004](0004-farm-areas-not-spots.md) | Farm areas, not spots | Accepted |
| [0005](0005-composable-stop-conditions.md) | Composable stop conditions | Accepted |
| [0006](0006-a-ticked-state-machine.md) | A ticked state machine | Accepted |
| [0007](0007-never-take-over-a-running-rotation.md) | Never take over a running rotation | Accepted |
| [0008](0008-learn-from-what-was-observed.md) | Learn from what was observed | Accepted |
| [0009](0009-core-split-for-testing.md) | A core library that never sees the game | Accepted |
| [0010](0010-installing-as-a-dev-plugin.md) | Installing as a dev plugin | Accepted |
| [0011](0011-crafting-lists-from-artisans-config.md) | Crafting lists from Artisan's config file | Accepted |
