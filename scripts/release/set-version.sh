#!/bin/sh
# Updates the one repository-owned version value without platform-specific sed -i behavior.
set -eu

fail() { printf '%s\n' "archy-version: $1" >&2; exit 64; }

root=$(CDPATH= cd "$(dirname "$0")/../.." && pwd)
version_file=${ARCHY_VERSION_FILE:-"$root/eng/Version.props"}
version=${1:-}

[ "$#" -eq 1 ] || fail "usage: $0 <X.Y.Z>"
sh "$root/scripts/release/version.sh" validate "$version"
[ -f "$version_file" ] || fail "version file is missing: $version_file"

temporary_file=$(mktemp "${version_file}.XXXXXX") || fail "could not create a temporary version file"
trap 'rm -f "$temporary_file"' 0 HUP INT TERM

awk -v version="$version" '
  /^[[:space:]]*<ArchyVersion>[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*<\/ArchyVersion>[[:space:]]*$/ {
    print "    <ArchyVersion>" version "</ArchyVersion>"
    updated++
    next
  }
  { print }
  END { if (updated != 1) exit 42 }
' "$version_file" > "$temporary_file" || fail "version file must contain exactly one ArchyVersion value"

mv "$temporary_file" "$version_file"
trap - 0 HUP INT TERM
