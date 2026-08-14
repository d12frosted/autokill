#!/usr/bin/env bash
#
# Install this plugin into a local XIV on Mac setup.
#
# AutoKill is not in any plugin repository, so rather than being copied into
# installedPlugins it is registered as a dev plugin load location: dalamud loads
# it straight out of the build directory. Rebuilding and reloading from the dev
# plugins tab then picks up changes with nothing to copy.
#
# The game runs under wine, where / is mounted as Z:, so the path handed to
# dalamud is the windows shaped one.
#
#   ./scripts/install.sh              build Debug and register
#   ./scripts/install.sh --release    build Release and register
#   ./scripts/install.sh --no-build   register whatever is already built
#   ./scripts/install.sh --dry-run    print what would happen, change nothing
#   ./scripts/install.sh --status     show what is built and what is registered
#   ./scripts/install.sh --uninstall  remove the registration, leave the build
#
# Override the setup location with XOM_ROOT=/some/path.

set -euo pipefail

PLUGIN="AutoKill"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
XOM_ROOT="${XOM_ROOT:-$HOME/Library/Application Support/XIV on Mac}"

CONFIG="Debug"
ACTION="install"
DRY=0
BUILD=1
FORCE=0

die() { printf 'error: %s\n' "$*" >&2; exit 1; }
info() { printf '%s\n' "$*"; }

while [ $# -gt 0 ]; do
    case "$1" in
        --release) CONFIG="Release" ;;
        --debug) CONFIG="Debug" ;;
        --no-build) BUILD=0 ;;
        --dry-run) DRY=1 ;;
        --status) ACTION="status" ;;
        --uninstall) ACTION="uninstall" ;;
        --force) FORCE=1 ;;
        -h|--help) awk 'NR>1 && !/^#/ {exit} NR>1 {sub(/^# ?/, ""); print}' "$0"; exit 0 ;;
        *) die "unknown argument: $1" ;;
    esac
    shift
done

BUILD_DIR="$REPO_ROOT/$PLUGIN/bin/$CONFIG"
DALAMUD_CONFIG="$XOM_ROOT/dalamudConfig.json"
BACKUP="$XOM_ROOT/dalamudConfig.json.autokill-backup"
PLUGIN_CONFIG_DIR="$XOM_ROOT/pluginConfigs/$PLUGIN"

[ -d "$XOM_ROOT" ] || die "XIV on Mac setup not found at: $XOM_ROOT (set XOM_ROOT to override)"
[ -f "$DALAMUD_CONFIG" ] || die "no dalamud config at: $DALAMUD_CONFIG"

# dalamud holds its configuration in memory and writes the whole file out when
# the game exits, so anything edited underneath a running game is thrown away.
assert_game_stopped() {
    [ "$DRY" -eq 1 ] && return 0
    if pgrep -f "ffxiv_dx11" >/dev/null 2>&1; then
        [ "$FORCE" -eq 1 ] || die "FFXIV looks like it is running - quit the game first (or pass --force)"
        info "warning: FFXIV appears to be running, dalamud will overwrite this on exit"
    fi
}

# / is mounted as Z: inside the wine prefix the game runs in
windows_path() {
    printf 'Z:%s' "$(printf '%s' "$1" | tr '/' '\\')"
}

# Reads or edits DevPluginLoadLocations. The list is serialised by Newtonsoft
# with $type and $values wrappers, so both that shape and a plain array are
# handled.
config_tool() {
    python3 - "$DALAMUD_CONFIG" "$(windows_path "$BUILD_DIR")" "$1" <<'PYTHON'
import json
import pathlib
import sys

config_path, target, action = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3]
LIST_TYPE = (
    "System.Collections.Generic.List`1[[Dalamud.Configuration.DevPluginLocationSettings, Dalamud]],"
    " System.Private.CoreLib"
)
ENTRY_TYPE = "Dalamud.Configuration.DevPluginLocationSettings, Dalamud"

config = json.loads(config_path.read_text())
node = config.get("DevPluginLoadLocations")

if isinstance(node, dict):
    values = node.setdefault("$values", [])
elif isinstance(node, list):
    values = node
else:
    node = config["DevPluginLoadLocations"] = {"$type": LIST_TYPE, "$values": []}
    values = node["$values"]

def matches(entry):
    return isinstance(entry, dict) and entry.get("Path", "").rstrip("\\") == target.rstrip("\\")

if action == "status":
    for entry in values:
        if matches(entry):
            print("enabled" if entry.get("IsEnabled", True) else "disabled")
            break
    else:
        print("absent")
    sys.exit(0)

if action == "add":
    for entry in values:
        if matches(entry):
            if entry.get("IsEnabled", True):
                print("already registered")
            else:
                entry["IsEnabled"] = True
                config_path.write_text(json.dumps(config, indent=2))
                print("re-enabled")
            sys.exit(0)

    values.append({"$type": ENTRY_TYPE, "Path": target, "IsEnabled": True})
    config_path.write_text(json.dumps(config, indent=2))
    print("registered")
    sys.exit(0)

if action == "remove":
    remaining = [entry for entry in values if not matches(entry)]
    if len(remaining) == len(values):
        print("not registered")
        sys.exit(0)
    if isinstance(node, dict):
        node["$values"] = remaining
    else:
        config["DevPluginLoadLocations"] = remaining
    config_path.write_text(json.dumps(config, indent=2))
    print("removed")
PYTHON
}

backup_once() {
    if [ ! -f "$BACKUP" ]; then
        info "backing up dalamud config to $(basename "$BACKUP")"
        [ "$DRY" -eq 1 ] || cp "$DALAMUD_CONFIG" "$BACKUP"
    fi
}

do_status() {
    info "setup:      $XOM_ROOT"
    info "build dir:  $BUILD_DIR"
    if [ -f "$BUILD_DIR/$PLUGIN.dll" ]; then
        info "  built:    $(date -r "$BUILD_DIR/$PLUGIN.dll" '+%Y-%m-%d %H:%M')"
        [ -f "$BUILD_DIR/$PLUGIN.json" ] || info "  warning:  no $PLUGIN.json beside the assembly, dalamud will not load it"
    else
        info "  built:    (nothing built yet)"
    fi
    info "dev plugin: $(config_tool status)"
    info "  path:     $(windows_path "$BUILD_DIR")"
    info "config:     $PLUGIN_CONFIG_DIR"
}

do_install() {
    assert_game_stopped

    if [ "$BUILD" -eq 1 ]; then
        info "building $CONFIG..."
        if [ "$DRY" -eq 1 ]; then
            info "  would: dotnet build $REPO_ROOT/$PLUGIN/$PLUGIN.csproj -c $CONFIG"
        else
            dotnet build "$REPO_ROOT/$PLUGIN/$PLUGIN.csproj" -c "$CONFIG" -v q --nologo
        fi
    fi

    if [ "$DRY" -eq 0 ]; then
        [ -f "$BUILD_DIR/$PLUGIN.dll" ] || die "no build output at $BUILD_DIR/$PLUGIN.dll"
        [ -f "$BUILD_DIR/$PLUGIN.json" ] || die "no manifest at $BUILD_DIR/$PLUGIN.json"
    fi

    backup_once

    if [ "$DRY" -eq 1 ]; then
        info "  would: register $(windows_path "$BUILD_DIR") as a dev plugin"
    else
        info "dev plugin: $(config_tool add)"
    fi

    info ""
    info "done. start the game and run /autokill."
    info "settings will land in: $PLUGIN_CONFIG_DIR"
    info "note: rebuilding is enough, then reload from dalamud's dev plugins tab."
}

do_uninstall() {
    assert_game_stopped
    backup_once
    if [ "$DRY" -eq 1 ]; then
        info "  would: remove $(windows_path "$BUILD_DIR") from the dev plugin locations"
    else
        info "dev plugin: $(config_tool remove)"
    fi
}

case "$ACTION" in
    status) do_status ;;
    install) do_install ;;
    uninstall) do_uninstall ;;
esac
