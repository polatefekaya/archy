#!/usr/bin/env zsh
set -euo pipefail

root="${0:A:h:h}"
cd "$root"

required=(
  docs/adr/0001-single-repository-workspace.md
  docs/adr/0002-native-aot-host-and-sidecars.md
  docs/adr/0003-single-sqlite-source-of-truth.md
  docs/adr/0004-csharp-first-extensible-providers.md
  docs/adr/0005-openai-provider-and-data-boundary.md
  docs/adr/0006-enforcement-at-delivery-boundaries.md
  docs/architecture/capability-matrix.md
  docs/architecture/bounded-contexts.md
  docs/architecture/graph-identity-revisions.md
  docs/architecture/compatibility-policy.md
  docs/architecture/data-handling-and-recovery.md
  docs/architecture/performance-budgets.md
)

for document in $required; do
  [[ -s "$document" ]] || { print -u2 "Missing required architecture contract: $document"; exit 1; }
done

rg -q "Native AOT" docs/adr/0002-native-aot-host-and-sidecars.md
rg -q "OpenAI Responses API" docs/adr/0005-openai-provider-and-data-boundary.md
rg -q "commit and required CI" docs/adr/0006-enforcement-at-delivery-boundaries.md
rg -q "workspace_id" docs/architecture/graph-identity-revisions.md
rg -q "Telemetry is off by default" docs/architecture/data-handling-and-recovery.md
