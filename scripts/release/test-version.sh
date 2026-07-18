#!/bin/sh
# Portable regression tests for the repository-owned semantic version tools.
set -eu

fail() { printf '%s\n' "archy-version-test: $1" >&2; exit 1; }
root=$(CDPATH= cd "$(dirname "$0")/../.." && pwd)
temporary_directory=$(mktemp -d "${TMPDIR:-/tmp}/archy-version-test.XXXXXX") || fail "could not create temporary directory"
trap 'rm -rf "$temporary_directory"' 0 HUP INT TERM
version_file="$temporary_directory/Version.props"
plugin_manifest="$temporary_directory/plugin.json"
cp "$root/eng/Version.props" "$version_file"
cp "$root/plugins/archy/.codex-plugin/plugin.json" "$plugin_manifest"

run() { ARCHY_VERSION_FILE="$version_file" ARCHY_PLUGIN_MANIFEST="$plugin_manifest" "$@"; }
version_tool="$root/scripts/release/version.sh"
set_tool="$root/scripts/release/set-version.sh"

current=$(run sh "$version_tool" current)
previous_ifs=$IFS
IFS=.
set -- $current
IFS=$previous_ifs
major=$1
minor=$2
patch=$3
[ "$(run sh "$version_tool" next initial)" = "$current" ] || fail "initial version was not preserved"
[ "$(run sh "$version_tool" next patch)" = "$major.$minor.$((patch + 1))" ] || fail "patch version was not incremented"
[ "$(run sh "$version_tool" next minor)" = "$major.$((minor + 1)).0" ] || fail "minor version was not incremented"
[ "$(run sh "$version_tool" next major)" = "$((major + 1)).0.0" ] || fail "major version was not incremented"
run sh "$set_tool" 2.4.6
[ "$(run sh "$version_tool" current)" = "2.4.6" ] || fail "version was not updated"
[ "$(sed -n 's/.*"version": "\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\)".*/\1/p' "$plugin_manifest")" = "2.4.6" ] || fail "plugin manifest version was not updated"

if run sh "$version_tool" validate 01.2.3 >/dev/null 2>&1; then
  fail "leading-zero version was accepted"
fi
if run sh "$version_tool" validate 1.2 >/dev/null 2>&1; then
  fail "incomplete version was accepted"
fi

printf '%s\n' "archy-version-test: passed"
