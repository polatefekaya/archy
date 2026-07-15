# Vertical-slice architecture

Archy is organized by bounded context and capability, never by a broad technical activity. A feature owns its request, Mediator handler, domain contracts, persistence adapter, validation, and tests when they change together.

## Layout rules

- Use `Features/<Context>/<Capability>/` for a concrete operation, such as `WorkspaceDatabase/Backup` or `Graph/CommitGraphRevision`.
- Use an aggregate folder only when several operations genuinely share one domain aggregate and its persistence boundary. Name its persistence port and implementation `<Aggregate>Repository`; do not use a generic `Store`, `Manage`, or `Maintain` bucket.
- Keep command-line parsing and rendering under `Features/CommandLine/<Context>/`. Domain slices must not contain `*Cli` adapters.
- Keep cross-cutting primitives in `SharedKernel`; do not create a catch-all feature folder for unrelated helpers.
- Put database lifecycle capabilities beneath `Storage/WorkspaceDatabase`: `Initialize`, `Integrity`, `Check`, `Backup`, `Restore`, `Vacuum`, and `ExportDiagnostics`. Each write or operational capability owns its command, handler, result contract, and service.

## Review checklist

Before adding a file, answer these questions:

1. Which user-visible capability or aggregate owns it?
2. Does it change for the same reason as its neighboring files?
3. Is an adapter (CLI, persistence, provider) living at the relevant boundary rather than inside an unrelated feature?
4. Would a second operation require a sibling slice rather than another method on a generic coordinator?

The architecture tests enforce the mechanical parts of this contract. Review still decides aggregate cohesion: an aggregate repository may own its lifecycle reads and writes, but unrelated operations must never be collected behind a generic façade.
