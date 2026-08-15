#!/bin/sh
# Exercises the user-visible install contract against a clean prefix.
set -eu

fail() { printf '%s\n' "archy-release-verify: $1" >&2; exit 64; }
archive=${1:-}
workspace=${2:-$(CDPATH= cd "$(dirname "$0")/../.." && pwd)}
[ -f "$archive" ] || fail "usage: sh scripts/release/verify-macos-release.sh <archive.tar.gz> [workspace]"
[ -d "$workspace/.git" ] || fail "workspace must be a Git repository"

temporary_directory=$(mktemp -d "${TMPDIR:-/tmp}/archy-release-verify.XXXXXX") || fail "could not create a temporary directory"
trap 'rm -rf "$temporary_directory"' 0 HUP INT TERM
tar -xzf "$archive" -C "$temporary_directory"
release_directory=$(find "$temporary_directory" -mindepth 1 -maxdepth 1 -type d -name 'archy-*-osx-*' -print | head -n 1)
[ -n "$release_directory" ] || fail "archive does not contain an Archy release directory"
if find "$release_directory" -type f \( -name '.DS_Store' -o -name '._*' \) -print | grep -q .; then
  fail "archive contains macOS metadata files"
fi

ARCHY_INSTALL_PREFIX="$temporary_directory/prefix" "$release_directory/install.sh"
installed="$temporary_directory/prefix/bin/archy"
[ -x "$installed" ] || fail "installer did not create the Archy executable"
[ -f "$temporary_directory/prefix/bin/libe_sqlite3.dylib" ] || fail "installer did not create the SQLite library"
[ -f "$temporary_directory/prefix/bin/wwwroot/index.html" ] || fail "installer did not install the web UI"
"$installed" workspace init --path "$workspace" --state-root "$temporary_directory/state" --json >/dev/null
ARCHY_INSTALL_PREFIX="$temporary_directory/prefix" "$release_directory/uninstall.sh"
[ ! -e "$temporary_directory/prefix/bin/archy" ] || fail "uninstaller left the Archy executable behind"
[ ! -e "$temporary_directory/prefix/share/archy" ] || fail "uninstaller left the owned installation behind"
printf '%s\n' "archy-release-verify: passed"
