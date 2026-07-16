#!/bin/sh
# Fails CI when a production JavaScript dependency has a high/critical advisory.
# NuGet output is retained as an artifact because remediation depends on the package owner.
set -eu

root=$(CDPATH= cd "$(dirname "$0")/../.." && pwd)
report_directory=${ARCHY_SECURITY_REPORT_DIRECTORY:-"$root/artifacts/security"}
mkdir -p "$report_directory"

dotnet list "$root/src/Archy/Archy.csproj" package --include-transitive --vulnerable > "$report_directory/dotnet-vulnerabilities.txt"
for package_directory in "$root/ui/archy-web" "$root/sidecars/jscpd" "$root/sidecars/louvain"; do
  name=$(basename "$package_directory")
  (cd "$package_directory" && npm ci --ignore-scripts && npm audit --omit=dev --audit-level=high --json) > "$report_directory/${name}-npm-audit.json"
done
