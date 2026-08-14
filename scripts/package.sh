#!/usr/bin/env bash
#
# Package a release and write the repository manifest.
#
# Dalamud installs third party plugins from a "custom plugin repository": a JSON
# file listing plugins and where to download each one. A user adds the URL of
# that file in Dalamud's settings and the plugin then appears in the installer
# like any other, with updates handled for them.
#
# This produces both halves:
#
#   dist/AutoKill.zip   the plugin, as Dalamud expects to receive it
#   repo.json           the manifest, committed and served from the repository
#
# Publishing is two steps and this only does the first. Afterwards, attach the
# zip to a release tagged v<version> and push the updated repo.json, or the
# manifest will point at a download that does not exist.
#
#   ./scripts/package.sh              package the current version
#   ./scripts/package.sh --version X  package as version X
#
set -euo pipefail

PLUGIN="AutoKill"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO_URL="https://github.com/d12frosted/autokill"
BRANCH="main"

VERSION=""
while [ $# -gt 0 ]; do
    case "$1" in
        --version) VERSION="${2:-}"; shift ;;
        -h|--help) awk 'NR>1 && !/^#/ {exit} NR>1 {sub(/^# ?/, ""); print}' "$0"; exit 0 ;;
        *) printf 'error: unknown argument: %s\n' "$1" >&2; exit 1 ;;
    esac
    shift
done

die() { printf 'error: %s\n' "$*" >&2; exit 1; }

# Dalamud compares the version in the manifest against the built assembly and
# refuses the update if they disagree, so both come from the project file.
if [ -z "$VERSION" ]; then
    VERSION="$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' "$REPO_ROOT/$PLUGIN/$PLUGIN.csproj" | head -1)"
fi
[ -n "$VERSION" ] || die "no <Version> in $PLUGIN.csproj and none given"

# Assembly versions are always four parts, whatever the project file says.
ASSEMBLY_VERSION="$VERSION"
while [ "$(printf '%s' "$ASSEMBLY_VERSION" | tr -cd '.' | wc -c | tr -d ' ')" -lt 3 ]; do
    ASSEMBLY_VERSION="$ASSEMBLY_VERSION.0"
done

BUILD_DIR="$REPO_ROOT/$PLUGIN/bin/Release"
DIST="$REPO_ROOT/dist"

printf 'building %s %s\n' "$PLUGIN" "$ASSEMBLY_VERSION"
dotnet build "$REPO_ROOT/$PLUGIN/$PLUGIN.csproj" -c Release -v q --nologo

[ -f "$BUILD_DIR/$PLUGIN.dll" ] || die "no build output at $BUILD_DIR"
[ -f "$BUILD_DIR/$PLUGIN.json" ] || die "no manifest beside the assembly"

rm -rf "$DIST"
mkdir -p "$DIST"

# Zipped from inside the build directory: Dalamud expects the assembly at the
# root of the archive, not inside a folder.
( cd "$BUILD_DIR" && zip -r -q "$DIST/$PLUGIN.zip" . -x '*.pdb' )
printf 'wrote %s (%s)\n' "$DIST/$PLUGIN.zip" "$(du -h "$DIST/$PLUGIN.zip" | cut -f1)"

DOWNLOAD="$REPO_URL/releases/download/v$VERSION/$PLUGIN.zip"
ICON="https://raw.githubusercontent.com/d12frosted/autokill/$BRANCH/$PLUGIN/images/icon.png"

python3 - "$REPO_ROOT/$PLUGIN/$PLUGIN.json" "$REPO_ROOT/repo.json" \
    "$ASSEMBLY_VERSION" "$DOWNLOAD" "$ICON" "$REPO_URL" <<'PYTHON'
import json
import sys

manifest_path, out_path, version, download, icon, repo_url = sys.argv[1:7]
manifest = json.load(open(manifest_path))

# The repository manifest is the plugin's own manifest plus where to get it.
# Install, update and testing all point at the same build: there is no separate
# testing channel, and Dalamud wants all three present.
entry = dict(manifest)
entry.update({
    "AssemblyVersion": version,
    "RepoUrl": repo_url,
    "IconUrl": icon,
    "DownloadLinkInstall": download,
    "DownloadLinkUpdate": download,
    "DownloadLinkTesting": download,
    "IsHide": False,
    "IsTestingExclusive": False,
})

with open(out_path, "w") as handle:
    json.dump([entry], handle, indent=2)
    handle.write("\n")

print(f"wrote {out_path}")
PYTHON

cat <<EOF

next:
  1. commit repo.json
  2. create a release tagged v$VERSION and attach dist/$PLUGIN.zip
  3. others add this in Dalamud's settings, under custom plugin repositories:

     https://raw.githubusercontent.com/d12frosted/autokill/$BRANCH/repo.json
EOF
