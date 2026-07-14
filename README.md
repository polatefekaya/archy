# Archy

Archy is a .NET 10 Native AOT architectural-memory tool for C# repositories.

The first implemented vertical slice locates the single Git repository that owns an Archy graph:

```bash
dotnet run --project src/Archy -- workspace locate --path . --json
```

Initialize external local state for that repository without writing it into the worktree:

```bash
dotnet run --project src/Archy -- workspace init --path . --json
```

Inspect the resolved, secret-free configuration before scanning:

```bash
dotnet run --project src/Archy -- config show --path . --json
```

Run the test suite and enforce the current product-coverage floor:

```bash
zsh scripts/test.sh
```

See [configuration](docs/configuration.md), [workspace-state coordination](docs/architecture/workspace-state.md), [the implementation backlog](docs/planning/zero-to-hero-implementation-backlog.md), and [vertical-slice conventions](docs/architecture/vertical-slice-ddd.md).

Implementation contracts: [capability matrix](docs/architecture/capability-matrix.md), [bounded contexts](docs/architecture/bounded-contexts.md), [graph identity/revisions](docs/architecture/graph-identity-revisions.md), [compatibility](docs/architecture/compatibility-policy.md), and [data handling/recovery](docs/architecture/data-handling-and-recovery.md).
