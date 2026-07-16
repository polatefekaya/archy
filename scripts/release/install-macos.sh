#!/bin/sh
# Installs only files from a verified Archy release directory.
set -eu

fail() { printf '%s\n' "archy-install: $1" >&2; exit 64; }
source_directory=${1:-$(CDPATH= cd "$(dirname "$0")" && pwd)}
prefix=${ARCHY_INSTALL_PREFIX:-"$HOME/.local"}
ownership_marker=.archy-installation
[ -f "$source_directory/release-manifest.json" ] || fail "run from an extracted Archy release directory"
[ -f "$source_directory/checksums.sha256" ] || fail "release checksum manifest is missing"
(cd "$source_directory" && shasum -a 256 -c checksums.sha256 >/dev/null) || fail "release integrity check failed"
[ -x "$source_directory/bin/archy" ] || fail "Archy binary is missing"
[ -f "$source_directory/bin/libe_sqlite3.dylib" ] || fail "Native SQLite library is missing"
[ -d "$source_directory/wwwroot" ] || fail "web UI assets are missing"
mkdir -p "$prefix/bin" "$prefix/share"
stage=$(mktemp -d "$prefix/share/.archy-stage.XXXXXX") || fail "could not create installation stage"
previous="$prefix/share/.archy-previous"
binary_stage=$(mktemp "$prefix/bin/.archy-binary.XXXXXX") || fail "could not stage the Archy binary"
sqlite_stage=$(mktemp "$prefix/bin/.archy-sqlite.XXXXXX") || fail "could not stage the SQLite library"
web_root="$prefix/bin/wwwroot"
web_stage="$prefix/bin/.archy-wwwroot-stage"
target="$prefix/share/archy"
had_previous=false
binary_hash=$(shasum -a 256 "$source_directory/bin/archy" | awk '{print $1}')
sqlite_hash=$(shasum -a 256 "$source_directory/bin/libe_sqlite3.dylib" | awk '{print $1}')

read_marker() { awk -F= -v key="$1" '$1 == key { print $2; exit }' "$target/$ownership_marker"; }
file_hash() { shasum -a 256 "$1" | awk '{print $1}'; }
verify_existing_installation() {
  if [ ! -e "$target" ]; then
    [ ! -e "$prefix/bin/archy" ] || fail "refusing to replace unowned $prefix/bin/archy"
    [ ! -e "$prefix/bin/libe_sqlite3.dylib" ] || fail "refusing to replace unowned $prefix/bin/libe_sqlite3.dylib"
    [ ! -e "$web_root" ] && [ ! -L "$web_root" ] || fail "refusing to replace unowned web assets at $web_root"
    return 0
  fi
  [ -f "$target/$ownership_marker" ] || fail "refusing to replace unowned installation at $target"
  [ "$(read_marker binary_sha256)" = "$(file_hash "$prefix/bin/archy")" ] || fail "refusing to replace a modified or unowned $prefix/bin/archy"
  [ "$(read_marker sqlite_sha256)" = "$(file_hash "$prefix/bin/libe_sqlite3.dylib")" ] || fail "refusing to replace a modified or unowned SQLite library"
  [ -L "$web_root" ] && [ "$(readlink "$web_root")" = "../share/archy/wwwroot" ] || fail "refusing to replace unowned web assets at $web_root"
}
rollback() {
  rm -f "$binary_stage"
  rm -f "$sqlite_stage" "$web_stage"
  rm -rf "$stage"
  if [ "$had_previous" = true ] && [ -d "$previous" ]; then
    rm -rf "$target"
    mv "$previous" "$target"
  elif [ "$had_previous" = false ]; then
    rm -rf "$target"
  fi
}
trap rollback 0 HUP INT TERM

verify_existing_installation
mkdir -p "$stage/plugins" "$stage/sidecars"
cp -R "$source_directory/plugins/archy" "$stage/plugins/archy"
cp -R "$source_directory/sidecars/jscpd" "$stage/sidecars/jscpd"
cp -R "$source_directory/sidecars/louvain" "$stage/sidecars/louvain"
cp -R "$source_directory/wwwroot" "$stage/wwwroot"
cp "$source_directory/release-manifest.json" "$stage/release-manifest.json"
cat > "$stage/$ownership_marker" <<EOF
schema=archy.installation/v1
binary_sha256=$binary_hash
sqlite_sha256=$sqlite_hash
EOF
install -m 755 "$source_directory/bin/archy" "$binary_stage"
install -m 755 "$source_directory/bin/libe_sqlite3.dylib" "$sqlite_stage"
rm -rf "$previous"
if [ -d "$target" ]; then had_previous=true; mv "$target" "$previous"; fi
mv "$stage" "$target"
mv "$binary_stage" "$prefix/bin/archy"
mv "$sqlite_stage" "$prefix/bin/libe_sqlite3.dylib"
ln -s ../share/archy/wwwroot "$web_stage"
rm -f "$web_root"
mv "$web_stage" "$web_root"
rm -rf "$previous"
trap - 0 HUP INT TERM
printf '%s\n' "archy-install: installed $prefix/bin/archy"
printf '%s\n' "archy-install: add $prefix/bin to PATH; Node and Python are required only for the corresponding optional sidecars."
