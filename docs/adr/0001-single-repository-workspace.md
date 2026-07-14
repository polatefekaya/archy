# ADR 0001: One workspace is one Git repository or monorepo

## Decision

An Archy workspace has exactly one canonical Git root and one local SQLite state database. A monorepo is one workspace. Archy does not create cross-repository graph edges.

## Consequences

Repository identity is the SHA-256 of its canonical root path for local-state routing. Every node, edge, run, revision, decision, and summary is scoped by that workspace ID. Linked Git worktrees resolve to their worktree root and remain independent local workspaces.

## Rejected alternatives

Federated graphs and automatic cross-repository joins are deferred: they cannot make sound ownership, revision, permission, or contract claims without an explicit federation protocol.
