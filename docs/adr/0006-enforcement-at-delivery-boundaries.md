# ADR 0006: Enforcement is authoritative at commit and merge

## Decision

New deterministic violations are blocked by `archy verify` at commit and required CI at protected-branch merge. Codex hooks provide post-edit guidance and may stop the current turn, but cannot prevent a local write.

## Consequences

Documentation must distinguish advisory findings from hard checks. Duplicate, cohesion, placement, and model findings never masquerade as pre-write enforcement.
