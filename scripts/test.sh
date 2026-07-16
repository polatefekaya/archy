#!/usr/bin/env zsh
set -euo pipefail

root="${0:A:h:h}"
results_root="${ARCHY_TEST_RESULTS_DIR:-$root/artifacts/test-results}"
cd "$root"
mkdir -p "$results_root"

sh "$root/scripts/release/test-version.sh"
dotnet restore Archy.sln
dotnet build Archy.sln --configuration Release --no-restore
dotnet test tests/Archy.UnitTests/Archy.UnitTests.csproj --configuration Release --no-build --no-restore
dotnet test tests/Archy.IntegrationTests/Archy.IntegrationTests.csproj --configuration Release --no-build --no-restore -p:CollectCoverage=true -p:CoverletOutput="$results_root/coverage/"
