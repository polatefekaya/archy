#!/bin/sh
# Portable regression tests for the repository-owned semantic version tools.
set -eu

fail() { printf '%s\n' "archy-version-test: $1" >&2; exit 1; }
root=$(CDPATH= cd "$(dirname "$0")/../.." && pwd)
temporary_directory=$(mktemp -d "${TMPDIR:-/tmp}/archy-version-test.XXXXXX") || fail "could not create temporary directory"
trap 'rm -rf "$temporary_directory"' 0 HUP INT TERM
version_file="$temporary_directory/Version.props"
plugin_manifest="$temporary_directory/plugin.json"
readme="$temporary_directory/README.md"
claude_manifest="$temporary_directory/claude-plugin.json"
marketplace="$temporary_directory/marketplace.json"
cp "$root/eng/Version.props" "$version_file"
cp "$root/plugins/archy/.codex-plugin/plugin.json" "$plugin_manifest"
cp "$root/README.md" "$readme"
cp "$root/plugins/claude-code/.claude-plugin/plugin.json" "$claude_manifest"
cp "$root/.claude-plugin/marketplace.json" "$marketplace"

run() { ARCHY_VERSION_FILE="$version_file" ARCHY_PLUGIN_MANIFEST="$plugin_manifest" ARCHY_README="$readme" ARCHY_CLAUDE_PLUGIN_MANIFEST="$claude_manifest" ARCHY_CLAUDE_MARKETPLACE="$marketplace" "$@"; }
version_tool="$root/scripts/release/version.sh"
set_tool="$root/scripts/release/set-version.sh"

current=$(run sh "$version_tool" current)
real_plugin_version=$(sed -n 's/.*"version": "\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\)".*/\1/p' "$root/plugins/archy/.codex-plugin/plugin.json")
[ "$real_plugin_version" = "$current" ] || fail "plugin manifest version $real_plugin_version does not match product version $current"
real_claude_version=$(sed -n 's/.*"version": "\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\)".*/\1/p' "$root/plugins/claude-code/.claude-plugin/plugin.json")
[ "$real_claude_version" = "$current" ] || fail "Claude Code plugin manifest version $real_claude_version does not match product version $current"
real_marketplace_version=$(sed -n 's/.*"version": "\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\)".*/\1/p' "$root/.claude-plugin/marketplace.json")
[ "$real_marketplace_version" = "$current" ] || fail "Claude Code marketplace version $real_marketplace_version does not match product version $current"
[ "$(grep -c "Version \`$current\`" "$root/README.md")" -eq 1 ] || fail "README product version does not match $current"
[ "$(grep -c "^ARCHY_VERSION=$current$" "$root/README.md")" -eq 1 ] || fail "README ARCHY_VERSION does not match $current"
[ "$(grep -c -- "--ref v$current" "$root/README.md")" -eq 1 ] || fail "README plugin tag does not match v$current"
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
[ "$(sed -n 's/.*"version": "\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\)".*/\1/p' "$claude_manifest")" = "2.4.6" ] || fail "Claude Code plugin manifest version was not updated"
[ "$(sed -n 's/.*"version": "\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\)".*/\1/p' "$marketplace")" = "2.4.6" ] || fail "Claude Code marketplace version was not updated"
[ "$(grep -c 'Version `2.4.6`' "$readme")" -eq 1 ] || fail "README product version was not updated"
[ "$(grep -c '^ARCHY_VERSION=2.4.6$' "$readme")" -eq 1 ] || fail "README ARCHY_VERSION was not updated"
[ "$(grep -c -- '--ref v2.4.6' "$readme")" -eq 1 ] || fail "README plugin tag was not updated"

if run sh "$version_tool" validate 01.2.3 >/dev/null 2>&1; then
  fail "leading-zero version was accepted"
fi
if run sh "$version_tool" validate 1.2 >/dev/null 2>&1; then
  fail "incomplete version was accepted"
fi

printf '%s\n' "archy-version-test: passed"
