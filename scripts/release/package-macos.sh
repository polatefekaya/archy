#!/bin/sh
# Produces an auditable Native AOT macOS archive for either supported architecture.
set -eu

fail() { printf '%s\n' "archy-release: $1" >&2; exit 64; }

root=$(CDPATH= cd "$(dirname "$0")/../.." && pwd)
version=${1:-}
architecture=${2:-}
[ "$#" -eq 2 ] || fail "usage: sh scripts/release/package-macos.sh <X.Y.Z> <arm64|x64>"
sh "$root/scripts/release/version.sh" validate "$version"
[ "$(sh "$root/scripts/release/version.sh" current)" = "$version" ] || fail "requested package version does not match eng/Version.props"
plugin_version=$(sed -n 's/.*"version": "\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\)".*/\1/p' "$root/plugins/archy/.codex-plugin/plugin.json")
[ "$plugin_version" = "$version" ] || fail "plugin manifest version does not match package version"
case "$architecture" in arm64|x64) ;; *) fail "architecture must be arm64 or x64" ;; esac

output_directory=${ARCHY_RELEASE_OUTPUT_DIRECTORY:-"$root/artifacts/release"}
temporary_directory=$(mktemp -d "${TMPDIR:-/tmp}/archy-release.XXXXXX") || fail "could not create a temporary directory"
trap 'rm -rf "$temporary_directory"' 0 HUP INT TERM
mkdir -p "$output_directory"

if [ "${ARCHY_RELEASE_NO_RESTORE:-false}" != true ]; then
  # Restore must use the exact Native AOT publish property set. Otherwise
  # --no-restore publish can miss ILCompiler's computed assembly inputs.
  # dotnet restore --runtime populates RuntimeIdentifiers but can leave Native AOT's
  # host-specific ILCompiler inputs selected when cross-publishing. Set the singular
  # RuntimeIdentifier property so the following --no-restore publish uses the target RID.
  dotnet restore "$root/src/Archy/Archy.csproj" -p:RuntimeIdentifier="osx-$architecture" \
    -p:SelfContained=true -p:PublishAot=true -p:PublishSingleFile=true
fi

dotnet publish "$root/src/Archy/Archy.csproj" \
  --configuration Release --runtime "osx-$architecture" --self-contained true \
  -p:PublishAot=true -p:PublishSingleFile=true -p:Version="$version" \
  -p:InformationalVersion="$version" --no-restore --output "$temporary_directory/publish"

binary="$temporary_directory/publish/Archy"
[ -x "$binary" ] || fail "Native AOT publish did not create the expected Archy executable"
[ "$("$binary" --version)" = "Archy $version" ] || fail "release binary version check failed"

stage="$temporary_directory/archy-$version-osx-$architecture"
mkdir -p "$stage/bin" "$stage/plugins" "$stage/sidecars"
install -m 755 "$binary" "$stage/bin/archy"
[ -f "$temporary_directory/publish/libe_sqlite3.dylib" ] || fail "Native AOT publish did not include libe_sqlite3.dylib"
install -m 755 "$temporary_directory/publish/libe_sqlite3.dylib" "$stage/bin/libe_sqlite3.dylib"
[ -d "$temporary_directory/publish/wwwroot" ] || fail "Native AOT publish did not include the web UI assets"
cp -R "$temporary_directory/publish/wwwroot" "$stage/wwwroot"
cp -R "$root/plugins/archy" "$stage/plugins/archy"
cp -R "$root/sidecars/jscpd" "$stage/sidecars/jscpd"
cp -R "$root/sidecars/louvain" "$stage/sidecars/louvain"
find "$stage" -type f \( -name '.DS_Store' -o -name '._*' \) -delete
cp "$root/scripts/release/install-macos.sh" "$stage/install.sh"
cp "$root/scripts/release/uninstall-macos.sh" "$stage/uninstall.sh"
chmod 755 "$stage/install.sh" "$stage/uninstall.sh"
cat > "$stage/release-manifest.json" <<EOF
{"schema":"archy.release-manifest/v1","version":"$version","platform":"macos","architecture":"$architecture","binary":"bin/archy","sidecars":[{"id":"jscpd","runtime":"node","path":"sidecars/jscpd"},{"id":"louvain","runtime":"python","path":"sidecars/louvain"}],"checksums":"checksums.sha256"}
EOF
(cd "$stage" && { find bin wwwroot plugins sidecars -type f -print; printf '%s\n' install.sh uninstall.sh release-manifest.json; } | LC_ALL=C sort | while IFS= read -r file; do shasum -a 256 "$file"; done) > "$stage/checksums.sha256"

archive_name="archy-$version-osx-$architecture.tar.gz"
archive_path="$output_directory/$archive_name"
tar -czf "$archive_path" -C "$temporary_directory" "archy-$version-osx-$architecture"
shasum -a 256 "$archive_path" | awk -v name="$archive_name" '{print $1 "  " name}' > "$archive_path.sha256"
printf '%s\n' "archy-release: created $archive_path"
