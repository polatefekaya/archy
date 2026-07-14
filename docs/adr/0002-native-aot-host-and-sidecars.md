# ADR 0002: Native AOT host with bounded sidecars

## Decision

The Archy host, CLI, local API, and MCP server target .NET 10 Native AOT. A Node or Python sidecar is permitted only for a capability that is materially better than a host implementation, with a versioned stdin/stdout protocol and no arbitrary shell input.

## Consequences

Host dependencies require trim/AOT analysis plus a real macOS AOT publish/run test. Sidecars cannot own facts, migrations, or durable state; their output is validated by Archy before persistence.

## Rejected alternatives

A managed-only host weakens distribution and runtime guarantees. Embedding dynamic language tooling into the AOT host weakens the dependency boundary.
