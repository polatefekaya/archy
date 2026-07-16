#!/bin/sh
# Removes only paths owned by a prior Archy installation under ARCHY_INSTALL_PREFIX.
set -eu

prefix=${ARCHY_INSTALL_PREFIX:-"$HOME/.local"}
case "$prefix" in ""|/) printf '%s\n' "archy-uninstall: refusing unsafe prefix" >&2; exit 64 ;; esac
target="$prefix/share/archy"
marker="$target/.archy-installation"
fail() { printf '%s\n' "archy-uninstall: $1" >&2; exit 64; }
[ -f "$marker" ] || fail "refusing to remove an installation without an ownership marker"
read_marker() { awk -F= -v key="$1" '$1 == key { print $2; exit }' "$marker"; }
file_hash() { shasum -a 256 "$1" | awk '{print $1}'; }
remove_owned_file() {
  path=$1
  expected=$2
  [ -e "$path" ] || return 0
  [ "$(file_hash "$path")" = "$expected" ] || fail "refusing to remove modified or unowned $path"
  rm -f "$path"
}

remove_owned_file "$prefix/bin/archy" "$(read_marker binary_sha256)"
remove_owned_file "$prefix/bin/libe_sqlite3.dylib" "$(read_marker sqlite_sha256)"
web_root="$prefix/bin/wwwroot"
if [ -e "$web_root" ] || [ -L "$web_root" ]; then
  [ -L "$web_root" ] && [ "$(readlink "$web_root")" = "../share/archy/wwwroot" ] || fail "refusing to remove unowned web assets at $web_root"
  rm -f "$web_root"
fi
rm -rf "$target"
printf '%s\n' "archy-uninstall: removed Archy from $prefix"
