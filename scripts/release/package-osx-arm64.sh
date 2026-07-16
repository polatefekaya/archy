#!/bin/sh
# Backward-compatible arm64 entry point. Use package-macos.sh for both architectures.
exec sh "$(CDPATH= cd "$(dirname "$0")" && pwd)/package-macos.sh" "$1" arm64
