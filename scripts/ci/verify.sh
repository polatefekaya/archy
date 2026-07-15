#!/bin/sh
# Provider-neutral Archy CI entrypoint. See docs/ci/provider-neutral.md for the release contract.
set -eu

fail() {
  printf '%s\n' "archy-ci: $1" >&2
  exit 64
}

resolve_directory() {
  CDPATH= cd "$1" && pwd
}

resolve_file() {
  directory=$(dirname "$1")
  name=$(basename "$1")
  printf '%s/%s\n' "$(resolve_directory "$directory")" "$name"
}

repository_root=$(resolve_directory "${ARCHY_REPOSITORY_ROOT:-$PWD}") || fail "ARCHY_REPOSITORY_ROOT is not a readable directory."
sarif_output=${ARCHY_SARIF_OUTPUT:-artifacts/archy.sarif}
case "$sarif_output" in
  /*) ;;
  *) sarif_output="$repository_root/$sarif_output" ;;
esac
mkdir -p "$(dirname "$sarif_output")"

temporary_directory=$(mktemp -d "${TMPDIR:-/tmp}/archy-ci.XXXXXX") || fail "could not create temporary CI storage."
state_root=${ARCHY_STATE_ROOT:-"$temporary_directory/state"}
owns_state_root=true
if [ -n "${ARCHY_STATE_ROOT:-}" ]; then
  owns_state_root=false
fi

cleanup() {
  rm -rf "$temporary_directory"
  if [ "$owns_state_root" = true ]; then
    rm -rf "$state_root"
  fi
}
trap cleanup 0 HUP INT TERM

if [ -n "${ARCHY_BIN:-}" ]; then
  archy_bin=$(resolve_file "$ARCHY_BIN") || fail "ARCHY_BIN does not resolve to a file."
  [ -x "$archy_bin" ] || fail "ARCHY_BIN must name an executable file."
else
  archy_version=${ARCHY_VERSION:-}
  archy_sha256=${ARCHY_SHA256:-}
  [ -n "$archy_version" ] || fail "ARCHY_VERSION is required when ARCHY_BIN is not supplied."
  [ -n "$archy_sha256" ] || fail "ARCHY_SHA256 is required when ARCHY_BIN is not supplied."
  case "$archy_version" in
    *[!0-9A-Za-z.-]*|"") fail "ARCHY_VERSION contains unsupported characters." ;;
  esac
  case "$archy_sha256" in
    *[!0123456789abcdefABCDEF]*|"") fail "ARCHY_SHA256 must be a hexadecimal SHA-256 digest." ;;
  esac
  [ "${#archy_sha256}" -eq 64 ] || fail "ARCHY_SHA256 must contain exactly 64 hexadecimal characters."

  case "$(uname -s)-$(uname -m)" in
    Darwin-arm64) runtime_identifier=osx-arm64 ;;
    *) fail "no verified Archy release is available for $(uname -s)-$(uname -m); current CI support is macOS arm64." ;;
  esac

  command -v curl >/dev/null 2>&1 || fail "curl is required to download an Archy release."
  command -v shasum >/dev/null 2>&1 || fail "shasum is required to verify the Archy release digest."
  command -v tar >/dev/null 2>&1 || fail "tar is required to extract the Archy release."
  release_base_url=${ARCHY_RELEASE_BASE_URL:-https://github.com/polatefekaya/archy/releases/download}
  archive_name="archy-${archy_version}-${runtime_identifier}.tar.gz"
  archive_path="$temporary_directory/$archive_name"
  release_url="${release_base_url%/}/v${archy_version}/${archive_name}"
  curl --fail --location --silent --show-error --proto '=https' --tlsv1.2 "$release_url" --output "$archive_path" ||
    fail "could not download $release_url."
  actual_sha256=$(shasum -a 256 "$archive_path" | awk '{print $1}')
  [ "$actual_sha256" = "$(printf '%s' "$archy_sha256" | tr '[:upper:]' '[:lower:]')" ] ||
    fail "the downloaded release digest does not match ARCHY_SHA256."
  release_directory="$temporary_directory/release"
  mkdir -p "$release_directory"
  tar -xzf "$archive_path" -C "$release_directory" || fail "could not extract the verified Archy release."
  archy_bin="$release_directory/archy"
  [ -x "$archy_bin" ] || fail "the verified release archive does not contain an executable named archy."
  reported_version=$("$archy_bin" --version) || fail "the downloaded Archy binary could not start."
  [ "$reported_version" = "Archy $archy_version" ] ||
    fail "the downloaded Archy binary reported '$reported_version', not Archy $archy_version."
fi

"$archy_bin" workspace init --path "$repository_root" --state-root "$state_root"
set +e
"$archy_bin" verify --path "$repository_root" --state-root "$state_root" --sarif --output "$sarif_output"
verification_status=$?
set -e

[ -s "$sarif_output" ] || fail "archy verify did not produce the requested SARIF report."
printf '%s\n' "archy-ci: SARIF report written to $sarif_output"
exit "$verification_status"
