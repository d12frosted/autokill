# 0010. Installing as a dev plugin

## Status

Accepted

## Context

AutoKill is not in any plugin repository, so there is no install button. Getting a locally
built assembly into a running game is its own problem, and two things about it are not
obvious. Both present identically, as Dalamud reporting that the path does not exist:

- The registered path must name the **assembly**, not the folder containing it. Dalamud
  tests it with `FileInfo.Exists`, which is false for a directory.
- `DevMode` has to be on. Dev plugin locations are only scanned when it is, so an
  otherwise perfect registration is skipped in silence.

Separately, XIV on Mac is an App Sandboxed application holding only
`files.user-selected.read-only`, so whether the game process can read a repository sitting
in a home directory is at best unproven.

## Decision

`scripts/install.sh` copies the build output into the XIV on Mac data directory and
registers the copied assembly as a dev plugin load location, turning `DevMode` on and
seeding automatic reloading.

The copy is a precaution rather than a diagnosed requirement: everything under the game's
own data directory is demonstrably readable by it, and a repository in a home directory is
not known to be.

Installing refuses to run while the game is up **only when a configuration change is
needed**. Dalamud holds its configuration in memory and writes it out on exit, so an edit
made underneath a running game is discarded. Copying over an already registered plugin
changes no configuration, so it is allowed mid-session and Dalamud reloads it by itself.

Installing also clears any earlier registration of this plugin, wherever it pointed, so
moving the install location does not leave Dalamud logging an error on every startup.

## Consequences

- After the first install, iteration is: build, copy, and the plugin reloads in place with
  the game still running.
- Uninstalling removes the registration and the copy, and turns `DevMode` back off only if
  nothing else is registered.
- The script is specific to XIV on Mac's layout. `XOM_ROOT` overrides the location, but a
  different launcher would need more than that.
