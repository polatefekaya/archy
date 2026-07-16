#!/bin/sh
# Reads, validates, and calculates stable Archy release versions.
set -eu

fail() { printf '%s\n' "archy-version: $1" >&2; exit 64; }

root=$(CDPATH= cd "$(dirname "$0")/../.." && pwd)
version_file=${ARCHY_VERSION_FILE:-"$root/eng/Version.props"}

is_component() {
  case "$1" in
    ''|*[!0-9]*) return 1 ;;
    0) return 0 ;;
    0*) return 1 ;;
    *) return 0 ;;
  esac
}

validate() {
  version=${1:-}
  previous_ifs=$IFS
  IFS=.
  set -- $version
  IFS=$previous_ifs
  [ "$#" -eq 3 ] || return 1
  is_component "$1" && is_component "$2" && is_component "$3"
}

read_version() {
  [ -f "$version_file" ] || fail "version file is missing: $version_file"
  version=$(sed -n 's/^[[:space:]]*<ArchyVersion>\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\)<\/ArchyVersion>[[:space:]]*$/\1/p' "$version_file")
  [ "$(printf '%s\n' "$version" | sed '/^$/d' | wc -l | tr -d ' ')" -eq 1 ] || fail "version file must contain exactly one ArchyVersion value"
  validate "$version" || fail "version file contains an invalid semantic version: $version"
  printf '%s\n' "$version"
}

command=${1:-}
case "$command" in
  current)
    [ "$#" -eq 1 ] || fail "usage: $0 current"
    read_version
    ;;
  validate)
    [ "$#" -eq 2 ] || fail "usage: $0 validate <X.Y.Z>"
    validate "$2" || fail "invalid semantic version: $2"
    ;;
  next)
    [ "$#" -eq 2 ] || fail "usage: $0 next <initial|patch|minor|major>"
    release_type=$2
    current=$(read_version)
    previous_ifs=$IFS
    IFS=.
    set -- $current
    IFS=$previous_ifs
    major=$1
    minor=$2
    patch=$3
    case "$release_type" in
      initial) printf '%s\n' "$current" ;;
      patch) printf '%s.%s.%s\n' "$major" "$minor" "$((patch + 1))" ;;
      minor) printf '%s.%s.0\n' "$major" "$((minor + 1))" ;;
      major) printf '%s.0.0\n' "$((major + 1))" ;;
      *) fail "release type must be initial, patch, minor, or major" ;;
    esac
    ;;
  *)
    fail "usage: $0 <current|validate|next>"
    ;;
esac
