# Archy vertical-slice DDD conventions

Archy is organized by business capability and use case, not by technical layer. A feature is placed at:

```text
src/Archy/Features/<bounded-context>/<use-case>/
```

For example, the first slice is `Features/Workspaces/LocateWorkspace`. As a slice grows, its Mediator request/query, handler, domain policy, endpoint or CLI adapter, data access, and feature-local tests remain in that directory. A slice may depend on the small shared kernel and on explicit contracts from another bounded context; it does not reach into another feature's persistence or internals.

`SharedKernel` is intentionally narrow: stable primitives, identifiers, result/error handling, time abstractions, and cross-cutting observability contracts only. It must not become a catch-all `Services`, `Repositories`, or `Utilities` directory.

The initial bounded contexts are Workspaces, Graph, Rules, Memory, Duplicates, Placement, Sessions, Integrations, and Visualization. New code belongs in the context that owns the business decision, with subfolders per use case rather than horizontal folders such as `Controllers`, `Handlers`, or `Repositories`. `Mediator.SourceGenerator` dispatches in-process commands, queries, and notifications; it is infrastructure for the slices, not a reason to create a central handler layer.
