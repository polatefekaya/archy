# Capability matrix

| Capability | Initial-release status | Enforcement/guarantee |
|---|---|---|
| Workspace discovery, state, config, lock | Implemented foundation | One Git root; macOS lock semantics |
| SQLite migrations, graph history, and versioned summaries | Implemented foundation | Transactional, append-only provenance |
| Database check, backup, restore, vacuum, diagnostics | Implemented foundation | Managed-state only; source files untouched |
| Scope-aware source inventory | Implemented foundation | Hash-only cache; Git/managed-state/symlink exclusions; deterministic incremental changes |
| C# project and solution discovery | Implemented foundation | Deterministic `.sln`, `.slnx`, and `.csproj` map with target frameworks, references, source roots, and parse diagnostics |
| C# syntax facts and immutable graph analysis | Implemented foundation | Roslyn syntax only; snapshot hashes verified; syntax errors never activate a partial graph |
| Language-semantic adapter contract | Implemented foundation | AOT-safe, versioned snapshot contract for symbols, definitions, references, calls, inheritance, types, syntax sites, ranges, capabilities, and diagnostics; providers are transport-agnostic |
| External LSP process protocol and stdio transport | Implemented foundation | Versioned stdio launch, typed initialize-capability profile, bounded Content-Length framing, response routing, cancellation, timeout/restart, stderr capture, and Native AOT mock transcript |
| Provider site-match contract | Implemented foundation | Versioned, Native-AOT JSON contract separates matcher evidence from graph emission; unresolved/unsupported/degraded states are explicit |
| Provider join-key contract | Implemented foundation | Versioned type/string/pattern/config strategies retain raw values, normalized comparison values, and normalization provenance |
| Provider edge-emission contract | Implemented foundation | Only matched sites with a validated join strategy and rationale can create bounded-confidence graph facts |
| Explicit .NET DI registration and consumption providers | Implemented foundation | Unambiguous generic AddScoped/AddSingleton/AddTransient registrations plus normal/primary constructor consumer paths; multiple bindings remain explicit ambiguity and known assembly scanning is reported, never guessed |
| Explicit .NET typed messaging provider | Implemented foundation | Unambiguous generic Publish/Send to explicit IConsumer contracts only; publish and send remain separate edge kinds |
| Raw RabbitMQ topology provider | Implemented foundation | Literal BasicPublish, QueueBind, and BasicConsume create exchange/queue paths; dynamic or incomplete routes remain diagnostics |
| Explicit .NET configuration read and JSON definition providers | Implemented foundation | Static reads and repository `appsettings*.json` definitions join virtual key nodes; dynamic, duplicate, malformed, missing, and environment-only cases remain explicit diagnostics |
| C# LSP document synchronization | Implemented foundation | Hash-verified full-text open/close notifications through the same profile-driven engine; each immutable run uses a fresh session and any synchronization failure invalidates that semantic batch |
| Standard-LSP semantic extraction | Implemented foundation | Declarative profile per language: extensions, markers, executable, arguments, language ID, identity prefix, symbol-kind mapping, and query bound. Hash-verified document symbols, references, and outgoing calls are normalized and committed atomically; definitions, inheritance, type facts, incoming calls, and semantic receiver resolution remain explicitly unavailable. |
| Other configuration providers | Planned | Non-`appsettings` JSON, environment snapshots, and other configuration sources remain separate providers |
| Graph revisions, versioned symbols, and bounded traversal | Implemented foundation | Immutable facts; revision-scoped dependency paths |
| Deterministic architecture verification and SARIF | Implemented foundation | `archy verify` refreshes analysis, reads one locked active revision, rejects unassigned/ambiguous code-layer coverage, evaluates configured confidence-1 edge kinds and hard-edge cycles, then classifies stable findings against committed `archy.baseline.json`. Committed `archy.exceptions.json` can suppress only one exact active finding until expiry. `--sarif` emits SARIF 2.1.0 from that exact result, with stable rule IDs, baseline/exception state, and repository-relative locations where evidence has one. |
| Local Git enforcement hooks | Implemented foundation | `archy hooks install` preserves existing pre-commit/pre-push hooks, evaluates exactly staged/pushed Git trees in isolated temporary workspaces, and restores preserved hooks on uninstall. A client can bypass hooks, so required CI remains mandatory for merge enforcement. |
| Provider-neutral CI entrypoint | Implemented foundation | `scripts/ci/verify.sh` accepts either a trusted local binary or a version-and-SHA-pinned macOS arm64 release, initializes disposable state, emits SARIF, and retains canonical verify exit semantics. Unsupported platforms fail explicitly until their release artifacts are verified. |
| Decisions, session replay, duplicate/cluster/health persistence | Implemented foundation | Append-only, revision-scoped provenance |
| Duplicates, placement, cohesion analysis | Planned | Explainable advisory only |
| OpenAI summaries/embeddings | Planned | Opt-in, user-keyed, budgeted |
| MCP tools and Codex hooks | Planned | Post-edit guidance; no pre-write claim |
| macOS | Implemented host target | Native AOT publish tested |
| Linux | Planned next platform | No current release claim |
| Other source languages | Standard-LSP semantic facts configurable now | Add a validated `language_server_profiles` entry; generic symbols, references, and outgoing calls need no host-language code, while language-specific syntax/framework providers remain future work |
