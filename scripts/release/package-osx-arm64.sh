#!/bin/sh
# Creates the immutable macOS arm64 release asset consumed by scripts/ci/verify.sh.
set -eu

fail() {
  printf '%s\n' "archy-release: $1" >&2
  exit 64
}

version=${1:-}
case "$version" in
  *[!0-9.]*|"") fail "usage: sh scripts/release/package-osx-arm64.sh <X.Y.Z>" ;;
esac

root=$(CDPATH= cd "$(dirname "$0")/../.." && pwd)
output_directory=${ARCHY_RELEASE_OUTPUT_DIRECTORY:-"$root/artifacts/release"}
mkdir -p "$output_directory"
temporary_directory=$(mktemp -d "${TMPDIR:-/tmp}/archy-release.XXXXXX") || fail "could not create a temporary release directory."
trap 'rm -rf "$temporary_directory"' 0 HUP INT TERM

if [ "${ARCHY_RELEASE_NO_RESTORE:-false}" != true ]; then
  dotnet restore "$root/src/Archy/Archy.csproj" --runtime osx-arm64
fi

dotnet publish "$root/src/Archy/Archy.csproj" \
  --configuration Release \
  --runtime osx-arm64 \
  --self-contained true \
  -p:PublishAot=true \
  -p:PublishSingleFile=true \
  -p:Version="$version" \
  -p:InformationalVersion="$version" \
  --no-restore \
  --output "$temporary_directory/publish"

binary="$temporary_directory/publish/Archy"
[ -x "$binary" ] || fail "Native AOT publish did not create the expected Archy executable."
reported_version=$("$binary" --version) || fail "the Native AOT release binary could not start."
[ "$reported_version" = "Archy $version" ] ||
  fail "the release binary reported '$reported_version', not Archy $version."

stage_directory="$temporary_directory/stage"
mkdir -p "$stage_directory"
cp "$binary" "$stage_directory/archy"
chmod 755 "$stage_directory/archy"
archive_name="archy-${version}-osx-arm64.tar.gz"
archive_path="$output_directory/$archive_name"
tar -czf "$archive_path" -C "$stage_directory" archy
digest=$(shasum -a 256 "$archive_path" | awk '{print $1}')
printf '%s  %s\n' "$digest" "$archive_name" > "$archive_path.sha256"
printf '%s\n' "archy-release: created $archive_path"
printf '%s\n' "archy-release: SHA-256 $digest"
