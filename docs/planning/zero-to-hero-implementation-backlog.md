# Archy — Zero-to-Hero Implementation Backlog

**Status:** approved product direction, implementation backlog (foundation implementation in progress)

**Last reconciled:** 2026-07-14

## Product decisions captured

- Archy is a serious, open-source product, not a demo or a reduced MVP.
- The long-running Archy host and its CLI/MCP/API surface are written for **.NET 10 Native AOT**.
- A narrowly-scoped, versioned Node or Python sidecar is allowed where it is materially better than reimplementing a mature capability. Sidecars are never the product's source of truth and never receive arbitrary shell input.
- The initial public release is a complete **C#/.NET repository** implementation. The provider protocol is language-independent from day one; adding a *framework* inside an already-supported language is configuration-led. Adding a new *language* also requires a parser/LSP adapter and language-specific string-expression handling, so it must never be inaccurately marketed as configuration-only.
- The initial supported platform is macOS. Linux is the next supported platform; Windows is intentionally not in this release target.
- One Archy workspace maps to one Git repository or one monorepo root. Cross-repository graph edges are out of scope by design.
- Archy uses DDD-oriented vertical slices at `Features/<bounded-context>/<use-case>`. A slice owns its `Mediator.SourceGenerator` request/query/notification, handler, domain policy, adapter, persistence mapping, and feature-local tests; `SharedKernel` contains only genuinely stable cross-cutting primitives and contracts.
- AI summaries and semantic duplicate detection use a user-supplied OpenAI API key through an `IModelProvider` abstraction. Codex itself calls Archy's MCP tools; the running Codex agent is not an inference service that Archy can invoke.
- Deterministic violations are **mandatory at the delivery boundary**: `archy verify` blocks commits and required CI blocks merges. A Codex `PostToolUse` hook stops the current turn after a violating edit and explains the fix, but it cannot prevent bytes from being written. Duplicate, cohesion, and placement findings remain advisory.
- Codex integration exposes **10 MCP tools**: `get_dependents`, `check_violation`, `get_module_rules`, `check_duplicate`, `suggest_placement`, `record_decision`, `get_decisions`, `start_session`, `end_session`, and `flush_summaries`. It uses **3 hooks**: `SessionStart`, `PostToolUse` matching `Bash|Edit|Write`, and `Stop`.

## Evaluation findings that shape the design

1. The graph, versioned semantic memory, decision log, and UI must share one transactional SQLite model. Separate per-feature stores would make replay, calibration, and health scoring inconsistent.
2. Native AOT must be a continuously verified property. Dependencies used by the host need trimming/AOT publish tests; dynamic analysis belongs behind an explicit sidecar process boundary.
3. The original hook concept must be described precisely. Codex hooks can inject guidance and halt a turn after a tool execution, but cannot serve as a pre-write security reference monitor. Git/CI is the authoritative non-bypassable boundary once branch protection requires the check.
4. A baseline is essential for existing repositories: enforce newly introduced deterministic violations by default, surface legacy violations, and permit narrow, audited exceptions. Otherwise adoption is blocked by historical debt.
5. Every edge needs provenance, source ranges, join keys, confidence, and a graph revision. Without these, "why does X depend on Y?", explainable advisories, incremental invalidation, and session replay cannot be correct.
6. `AGENTS.md` is a generated context snapshot, not a real-time enforcement channel. Archy must update only a marked managed section and state that a new Codex task/session is needed to pick it up.
7. Semantic work must be failure-tolerant. A failed model call may leave summaries stale, but it must never corrupt the graph, block deterministic verification, or silently invent a summary.

## Delivery rules for every task

- Implement production code and automated tests together; a task is incomplete if its stated verification cannot run in CI.
- Do not add runtime reflection, dynamic code loading, or a package with unverified AOT behavior to the host without a documented exception and an AOT publish test.
- Use immutable graph revisions and append-only audit/version records where history is required. Do not overwrite a summary, decision, or session event that replay needs.
- Every public API, CLI command, MCP tool, hook payload, sidecar message, and database migration receives a compatibility/versioning test.
- Preserve user-owned files and configuration. Generated content must be isolated by explicit begin/end markers and be idempotent.

---

## Phase 0 — Architectural contracts and proof gates

### T-000 — Create the architecture decision record set
**Depends on:** —  
**Work:** Create ADRs for the single-repository boundary, Native AOT host/sidecar split, SQLite ownership, C#-first support, OpenAI provider abstraction, and mandatory pre-merge enforcement.  
**Done when:** Each decision records alternatives, consequences, non-goals, and the task IDs that implement it.  
**Verify:** A documentation test checks that every decision listed in "Product decisions captured" has one ADR.

### T-001 — Define the product capability matrix
**Depends on:** T-000  
**Work:** Publish a matrix of every provider, rule, UI view, MCP tool, sidecar, and platform with `implemented`, `advisory`, `hard-at-commit`, or `future-language` status.  
**Done when:** The matrix distinguishes C# initial-release coverage from extension points without hiding unsupported behavior.  
**Verify:** Review against all sections of the original specification; no feature is unclassified.

### T-002 — Define the internal bounded-context map
**Depends on:** T-000  
**Work:** Define vertical-slice ownership and allowed dependencies for the Workspaces, Graph, Rules, Memory, Duplicates, Placement, Sessions, Integrations, and Visualization bounded contexts; keep `Mediator.SourceGenerator` commands/queries/notifications, policies, adapters, persistence mappings, and feature tests co-located by use case.  
**Done when:** The dependency direction and prohibition on horizontal `Services`/`Repositories`/`Controllers` folders are written as machine-readable architecture rules Archy can eventually enforce on itself.  
**Verify:** A graph fixture containing one reverse dependency fails the rule test.

### T-003 — Define graph identity and revision invariants
**Depends on:** T-000  
**Work:** Specify stable IDs for repository, file, symbol, virtual resource (table/key/config), edge, graph revision, and analysis run.  
**Done when:** Renaming, deleting, re-adding, and moving a file have deterministic identity semantics.  
**Verify:** Fixture tests assert IDs and history outcomes for each operation.

### T-004 — Define the compatibility policy
**Depends on:** T-000  
**Work:** Set semantic-versioning rules for CLI output, config, schema migrations, MCP tools, hook JSON, and sidecar protocols.  
**Done when:** Each surface declares how version negotiation, deprecation, and incompatible upgrades work.  
**Verify:** Contract-test harness can run current and previous fixture payloads.

### T-005 — Define failure and recovery semantics
**Depends on:** T-003  
**Work:** Write explicit behavior for interrupted scans, unavailable LSP, sidecar failure, model timeouts, SQLite lock contention, corrupted cache, and interrupted migration.  
**Done when:** Every failure has a user-visible state, retry policy, and data-integrity invariant.  
**Verify:** Chaos tests execute one interruption for every listed category.

### T-006 — Define data handling and privacy policy
**Depends on:** T-000  
**Work:** State exactly which source fragments, summaries, hashes, embeddings, prompts, logs, and API metadata are persisted locally or sent to OpenAI.  
**Done when:** The policy covers opt-in AI, redaction, retention, deletion, and telemetry defaults.  
**Verify:** A fixture with fake secrets proves redaction before outbound model requests and log writes.

### T-007 — Establish performance budgets
**Depends on:** T-003  
**Work:** Set measurable budgets for cold scan, one-file incremental scan, hook response, SQLite traversal, UI initial load, and replay seek at small/medium/large repository sizes.  
**Done when:** Dataset sizes and pass/fail thresholds are committed.  
**Verify:** Benchmark project emits machine-readable results suitable for CI trend tracking.

## Phase 1 — Repository, build, and Native AOT foundation

### T-010 — Scaffold the solution topology
**Depends on:** T-002  
**Work:** Create projects for the AOT host, CLI, core contracts, storage, analysis, providers, rules, model integrations, MCP, web API, sidecar adapters, and tests.  
**Done when:** Project references follow the bounded-context map and no circular project references exist.  
**Verify:** `dotnet build` plus an architecture-reference test pass.

### T-011 — Pin the .NET 10 toolchain
**Depends on:** T-010  
**Work:** Add `global.json`, central package management, deterministic build settings, analyzers, nullable reference types, and warnings-as-errors.  
**Done when:** Developers and CI resolve the same SDK and dependency versions.  
**Verify:** Clean checkout build reproduces package lock data.

### T-012 — Add macOS Native AOT publish targets
**Depends on:** T-010  
**Work:** Configure self-contained `osx-arm64` and `osx-x64` publishing for the host/CLI, including trimming and invariant globalization decisions.  
**Done when:** Both artifacts start and run `archy --version` without a local .NET runtime.  
**Verify:** CI publishes, runs, and smoke-tests both artifacts in clean macOS environments.

### T-013 — Add the Native AOT compatibility gate
**Depends on:** T-012  
**Work:** Make publish warnings/errors, trim analysis, startup smoke test, and executable-size report required CI checks.  
**Done when:** A newly introduced AOT warning fails the pull request.  
**Verify:** A controlled warning fixture proves the gate fails.

### T-014 — Create the composition root
**Depends on:** T-010  
**Work:** Implement dependency registration with compile-time-safe constructors, explicit options binding, `Mediator.SourceGenerator` dispatch, and no runtime assembly scanning.  
**Done when:** All host services and vertical-slice request handlers can be instantiated in an integration test without reflection-based registration.  
**Verify:** A startup test enumerates required services and starts the CLI host.

### T-015 — Implement structured logging and correlation
**Depends on:** T-014  
**Work:** Add JSON logs with repository ID, graph revision, analysis run ID, session ID, operation name, and redaction middleware.  
**Done when:** One scan, hook, model call, and sidecar call can be correlated without source leakage.  
**Verify:** Log-contract test validates required fields and secret redaction.

### T-016 — Implement health and diagnostic endpoints
**Depends on:** T-014  
**Work:** Expose local health checks for database reachability, LSP state, sidecar readiness, model configuration, and watcher state.  
**Done when:** Each dependency reports healthy, degraded, or unavailable with an actionable reason.  
**Verify:** Integration tests force each degraded state.

### T-017 — Create deterministic fixture repositories
**Depends on:** T-010  
**Work:** Add small C# fixture repositories covering DI, EF Core, MassTransit/NServiceBus-shaped messaging, raw RabbitMQ, Redis/cache patterns, config, cycles, duplicates, and legacy violations.  
**Done when:** Fixtures are independent Git repositories with seeded commits and expected graph assertions.  
**Verify:** Test bootstrap can clone/reset every fixture without network access.

### T-018 — Add end-to-end test orchestration
**Depends on:** T-012, T-017  
**Work:** Create an AOT-host test launcher that initializes a fixture, runs scan/verify/MCP/web scenarios, and gathers artifacts on failure.  
**Done when:** One command executes the product-level acceptance suite.  
**Verify:** CI stores database, logs, screenshots, and sidecar transcripts for failed runs.

## Phase 2 — Workspace, configuration, and secure local state

### T-020 — Define `archy.toml` schema
**Depends on:** T-004  
**Work:** Specify repository identity, language server overrides, provider patterns, layer rules, model settings, sidecar settings, ignore rules, and health weights.  
**Done when:** The schema has defaults, validation errors, comments/examples, and a version field.  
**Verify:** Valid, unknown, deprecated, and invalid-key fixtures yield expected diagnostics.

### T-021 — Implement configuration discovery and precedence
**Depends on:** T-020  
**Work:** Load user defaults, repository `archy.toml`, and explicit CLI overrides with deterministic precedence.  
**Done when:** The effective configuration can be printed without leaking secrets.  
**Verify:** Precedence fixture asserts every override rule.

### T-022 — Implement repository-root discovery
**Depends on:** T-020  
**Work:** Resolve the enclosing Git root, reject nested accidental workspace roots, and support a monorepo root as one repository graph.  
**Done when:** Invocations from subdirectories resolve one stable workspace ID.  
**Verify:** Nested-directory tests return the same root and repository ID.

### T-023 — Create Archy local-state layout
**Depends on:** T-022  
**Work:** Define per-repository paths for SQLite, logs, model cache, sidecar cache, lock files, generated artifacts, and diagnostics.  
**Done when:** State is outside source control by default and can be redirected by configuration.  
**Verify:** `git status` stays clean after scan, serve, and hook operations.

### T-024 — Implement workspace locking
**Depends on:** T-023  
**Work:** Add cross-process read/write coordination so scan, watcher, hook, MCP, and UI operations cannot interleave corrupting writes.  
**Done when:** Contention has bounded wait/fail behavior and no partially visible revision.  
**Verify:** Parallel-process test runs scan and hook validation repeatedly.

### T-025 — Implement secret resolution
**Depends on:** T-020  
**Work:** Resolve the OpenAI API key from environment or macOS Keychain; never persist it in `archy.toml` or logs.  
**Done when:** Model calls work with either source and diagnostics identify only the source type.  
**Verify:** Repository-wide secret scan and unit tests confirm no key persistence.

### T-026 — Implement ignore and scope rules
**Depends on:** T-020, T-022  
**Work:** Merge `.gitignore`, default generated-folder exclusions, and explicit Archy include/exclude rules.  
**Done when:** The scanner can explain why any path was included or excluded.  
**Verify:** Ignore fixtures assert binary, generated, vendored, and explicitly included files.

### T-027 — Implement first-run bootstrap
**Depends on:** T-021, T-023  
**Work:** Create state, validate configuration, detect C#, initialize the database, and offer a non-destructive sample config.  
**Done when:** A new repository becomes analyzable in one explicit command without overwriting user files.  
**Verify:** Clean fixture bootstrap is idempotent.

## Phase 3 — SQLite graph store and immutable history

### T-030 — Implement migration runner
**Depends on:** T-010, T-004  
**Work:** Add ordered, checksummed, transactional SQLite migrations with upgrade, downgrade policy, and backup-before-migrate behavior.  
**Done when:** Schema version is auditable and interrupted migrations recover safely.  
**Verify:** Upgrade every historical migration fixture and simulate interruption.

### T-031 — Create repository and analysis-run tables
**Depends on:** T-030  
**Work:** Store repository identity, configuration hash, analyzer version, start/end time, status, commit SHA, and graph revision for every run.  
**Done when:** Every generated fact is traceable to an analysis run.  
**Verify:** Scan integration test joins all graph rows to one successful run.

### T-032 — Create node and node-version tables
**Depends on:** T-030, T-003  
**Work:** Persist files, directories, namespaces, types, methods, functions, virtual resources, content hashes, source ranges, and validity revisions.  
**Done when:** Historical node state remains queryable after edit, rename, or deletion.  
**Verify:** Rename/delete fixture preserves history and closes prior validity ranges.

### T-033 — Create edge and edge-version tables
**Depends on:** T-030, T-003  
**Work:** Persist source/destination IDs, kind, provider, join strategy/key, confidence, source evidence, and validity revisions.  
**Done when:** One edge can be explained from stored evidence without re-analysis.  
**Verify:** Edge provenance fixture compares expected source range and join key.

### T-034 — Create symbol and interface-fingerprint tables
**Depends on:** T-030  
**Work:** Persist fully qualified symbol identity, visibility, normalized signature, parameter/return metadata, and signature hash versions.  
**Done when:** Public-surface changes are deterministic and independent from model output.  
**Verify:** Signature-change fixture detects add/remove/type/visibility changes.

### T-035 — Create summary and summary-version tables
**Depends on:** T-030  
**Work:** Store summary text, English diff, source graph revision, commit SHA, model/provider metadata, staleness state, and supersession links.  
**Done when:** No prior summary is overwritten.  
**Verify:** Two updates to one node return both versions in chronological order.

### T-036 — Create decisions, sessions, and events tables
**Depends on:** T-030  
**Work:** Persist decision targets, type, resolution, note, actor, event time, session identity, and ordered session events.  
**Done when:** Decisions and replay events are append-only and queryable by edge/node/session.  
**Verify:** Replay fixture reconstructs a known event sequence exactly.

### T-037 — Create duplicate, cluster, and health tables
**Depends on:** T-030  
**Work:** Persist duplicate signal observations, aggregate findings, clustering revisions, metric components, health snapshots, and resolution links.  
**Done when:** Health score and advisory history can be reproduced from stored facts.  
**Verify:** Deterministic fixture recomputes the stored score.

### T-038 — Add traversal and lookup indexes
**Depends on:** T-032, T-033  
**Work:** Index active node/edge lookups, reverse edges, revision validity, path search, joins, and session ordering; add recursive CTE query helpers.  
**Done when:** Direct and transitive dependency queries meet the established budget.  
**Verify:** Query-plan assertions and benchmark thresholds pass.

### T-039 — Implement graph-revision transaction boundary
**Depends on:** T-031, T-038  
**Work:** Stage all facts for one analysis run and atomically expose a new active revision only after integrity checks succeed.  
**Done when:** Readers see either the previous complete graph or the next complete graph, never a mix.  
**Verify:** Kill-process-during-scan test leaves the prior revision active.

### T-040 — Implement database integrity and backup commands
**Depends on:** T-030, T-039  
**Work:** Add `archy db check`, backup, restore, vacuum policy, and diagnostics export.  
**Done when:** Users can validate and recover local state without source loss.  
**Verify:** Backup/restore round-trip reproduces active revision and history counts.

## Phase 4 — File inventory, C# semantics, and LSP adapter

### T-050 — Implement source inventory and hashing
**Depends on:** T-026, T-032  
**Work:** Enumerate in-scope files, classify language, hash content, and compute added/changed/deleted sets.  
**Done when:** Unchanged files are not reparsed during incremental scans.  
**Verify:** Hash-cache fixture records parse calls for only changed files.

### T-051 — Implement C# project and solution discovery
**Depends on:** T-050  
**Work:** Discover `.sln`, `.slnx`, and `.csproj` files, target frameworks, project references, generated-code settings, and source roots.  
**Done when:** Multi-project C# repositories produce a deterministic project map.  
**Verify:** Fixture with solution, standalone project, and nested project asserts map output.

### T-052 — Define the language-semantic adapter contract
**Depends on:** T-002, T-003  
**Work:** Define AOT-safe contracts for document symbols, definitions, references, calls, inheritance, types, diagnostics, source ranges, and syntax-site facts.  
**Done when:** Provider code consumes this contract rather than an LSP-specific type.  
**Verify:** A fake adapter drives provider tests without an LSP process.

### T-053 — Define the external LSP process contract
**Depends on:** T-052, T-005  
**Work:** Specify startup arguments, JSON-RPC framing, timeout/restart behavior, capability negotiation, stderr capture, and version reporting for a language-server sidecar.  
**Done when:** The protocol is documented and versioned independently from the host.  
**Verify:** Mock LSP transcript tests cover initialization, timeout, malformed response, and restart.

### T-054 — Implement Native AOT JSON-RPC/LSP client
**Depends on:** T-053, T-013  
**Work:** Implement the stdio transport, request IDs, cancellation, message framing, response routing, and diagnostic logging without dynamic serialization.  
**Done when:** The AOT host completes initialize/shutdown and concurrent requests against a mock server.  
**Verify:** AOT integration test runs the complete mock transcript.

### T-055 — Implement C# LSP autodetection and override
**Depends on:** T-051, T-053, T-020  
**Work:** Detect C# markers, select the configured default server, and honor per-repository command/argument overrides.  
**Done when:** Missing or incompatible server failures are explicit and do not corrupt graph state.  
**Verify:** Detection and override fixtures assert selected command and diagnostics.

### T-056 — Implement C# document synchronization
**Depends on:** T-054, T-055  
**Work:** Open/close changed documents, synchronize versions, and invalidate server state after solution/configuration changes.  
**Done when:** Semantic requests observe current buffer contents during an incremental run.  
**Verify:** Edit-before-query fixture returns the new symbol target.

### T-057 — Implement symbol and definition extraction
**Depends on:** T-052, T-056  
**Work:** Build nodes and source evidence from document symbols and definitions, including namespace/type/member containment.  
**Done when:** Public and private C# declarations get stable, fully-qualified identities.  
**Verify:** Golden fixture asserts node tree and source ranges.

### T-058 — Implement references, inheritance, and call extraction
**Depends on:** T-057  
**Work:** Collect resolvable import/using, call, reference, and inheritance relationships with target symbol identity.  
**Done when:** Source-derived edge kinds have semantically resolved destinations where LSP supplies them.  
**Verify:** Golden graph asserts each edge kind and no false unresolved target.

### T-059 — Implement type lookup for provider joins
**Depends on:** T-056  
**Work:** Resolve generic arguments, constructor parameter types, implemented interfaces, and aliases to canonical symbol identities.  
**Done when:** Provider type equality uses identity, not rendered type text.  
**Verify:** Alias and same-short-name fixtures join only correct symbols.

### T-060 — Implement C# syntax-site fact adapter
**Depends on:** T-052, T-013  
**Work:** Prove and select the implementation that exposes invocation, attribute, interpolation, literal, and declaration sites to providers while preserving the AOT host boundary; use a bounded sidecar if a direct implementation is not AOT-safe.  
**Done when:** The chosen adapter has a compatibility report, protocol test, and complete source-range facts for all provider fixtures.  
**Verify:** Native AOT publish plus fixture assertions for calls, attributes, interpolations, and literals pass.

### T-061 — Implement method metrics extraction
**Depends on:** T-060  
**Work:** Compute method LOC, branch/cyclomatic complexity, declaration kind, and data-only classification.  
**Done when:** Duplicate detection can exclude DTOs/records and bucket comparable methods.  
**Verify:** Metric golden tests cover branches, expressions, records, and generated code.

### T-062 — Implement semantic degradation handling
**Depends on:** T-055, T-059  
**Work:** Mark facts unavailable or unresolved with reasons rather than fabricating edges when LSP/project restore is unavailable.  
**Done when:** UI, verification, and providers can distinguish "not found" from "not analyzable."  
**Verify:** Broken-project fixture records degraded capability and preserves prior valid revision.

### T-063 — Implement LSP capability profiling and enforcement-edge policy
**Depends on:** T-053, T-058, T-062  
**Work:** Probe and record support for definition, references, incoming/outgoing call hierarchy, type lookup, and document symbols; define which resolved edge kinds/confidence tiers are eligible for deterministic layer/cycle enforcement and which remain advisory.  
**Done when:** A missing LSP capability degrades only its dependent analysis, and no low-confidence/string-joined/dynamic edge can accidentally create a hard violation.  
**Verify:** Capability-matrix fixtures assert per-method fallback behavior and rule tests prove low-confidence edges cannot block verification.

## Phase 5 — Generic provider engine and C# provider catalog

### T-070 — Define site-matcher contract
**Depends on:** T-052, T-060  
**Work:** Model provider site matches with language, framework, shape, source evidence, captures, and diagnostic state.  
**Done when:** A provider declares match intent without writing graph rows directly.  
**Verify:** Fake matcher fixtures serialize/deserialize contract payloads.

### T-071 — Define join-key extractor contract
**Depends on:** T-070  
**Work:** Model type-equality, string-equality, canonical-pattern, and config-path join keys with normalization metadata.  
**Done when:** Keys retain both raw evidence and normalized comparison value.  
**Verify:** Equality tests cover case, namespace, interpolation, and duplicate literals.

### T-072 — Define edge-emission and confidence contract
**Depends on:** T-071, T-033  
**Work:** Centralize edge kind, provider, confidence tier, rationale, and evidence validation.  
**Done when:** Providers cannot emit unproven edges without a join strategy and rationale.  
**Verify:** Invalid edge emission is rejected by unit tests.

### T-073 — Implement declarative C# pattern-table loader
**Depends on:** T-020, T-070  
**Work:** Load versioned built-in and repository-level framework method/type/attribute patterns without executable plugin code.  
**Done when:** A configuration change can add a supported-framework pattern without recompiling Archy.  
**Verify:** Custom-pattern fixture emits expected site facts and edges.

### T-074 — Implement provider execution scheduler
**Depends on:** T-072, T-039  
**Work:** Run enabled providers against one immutable semantic snapshot, isolate faults, collect diagnostics, and commit their facts atomically.  
**Done when:** One provider crash degrades only that provider and leaves prior graph active.  
**Verify:** Forced provider exception test validates failure isolation.

### T-075 — Implement type-equality join resolver
**Depends on:** T-059, T-071  
**Work:** Join captures by canonical C# symbol identity across project boundaries and record high confidence.  
**Done when:** Equivalent symbols join despite aliases and textual spelling differences.  
**Verify:** Cross-project generic-type fixture produces exactly one high-confidence edge.

### T-076 — Implement string and canonical-pattern join resolver
**Depends on:** T-071  
**Work:** Join normalized literals/patterns, retain placeholder metadata, and downgrade confidence when variable semantics differ.  
**Done when:** `order:{}` patterns join while `orderId`/`invoiceId` differences remain explainable.  
**Verify:** Cache-pattern fixture asserts join key, metadata, and confidence tier.

### T-077 — Implement explicit .NET DI registration provider
**Depends on:** T-073, T-075  
**Work:** Recognize `AddTransient`, `AddScoped`, `AddSingleton`, keyed variants, factory registrations, and configured equivalents; emit interface-to-implementation registrations only when statically explicit.  
**Done when:** Registration evidence identifies lifetime and generic service/implementation identities.  
**Verify:** DI fixture asserts every supported registration and rejects reflection scanning.

### T-078 — Implement DI consumption provider
**Depends on:** T-077, T-059  
**Work:** Identify constructor-injected service types and join them to explicit registrations; model unresolved/multiple registrations separately.  
**Done when:** Consumer-to-service and service-to-implementation paths are traversable without guessing.  
**Verify:** Multiple-registration fixture records ambiguity rather than a false unique edge.

### T-079 — Record unsupported DI mechanisms
**Depends on:** T-077  
**Work:** Detect known assembly scanning/reflection registration shapes and emit a visible coverage limitation diagnostic, not a binding edge.  
**Done when:** Users can see why a service graph is incomplete.  
**Verify:** Scrutor-style fixture reports the limitation with source evidence.

### T-080 — Implement typed message producer provider
**Depends on:** T-073, T-075  
**Work:** Recognize configurable MassTransit/NServiceBus-style `Publish<T>` and `Send<T>` patterns and emit producer-to-message edges.  
**Done when:** Generic message identity is resolved across projects.  
**Verify:** Producer fixture produces a high-confidence typed edge.

### T-081 — Implement typed message consumer provider
**Depends on:** T-073, T-075  
**Work:** Recognize configurable `IConsumer<T>`/handler implementations and emit consumer-to-message edges.  
**Done when:** Producer, message, and consumer form a queryable path.  
**Verify:** End-to-end message fixture finds consumers from a producer node.

### T-082 — Implement string-routed RabbitMQ provider
**Depends on:** T-073, T-076  
**Work:** Recognize `BasicPublish`, `BasicConsume`, and queue-binding calls; extract exchange/queue/routing literals and emit lower-confidence routing edges.  
**Done when:** Static literals join and dynamic/no-literal routes are explicitly unresolved.  
**Verify:** Literal and dynamic routing fixtures assert opposite outcomes.

### T-083 — Implement EF Core entity/table provider
**Depends on:** T-073, T-075, T-076  
**Work:** Recognize `DbSet<T>`, `[Table]`, and statically visible fluent table mappings; create table resource nodes and entity-to-table edges.  
**Done when:** Explicit table names take precedence over convention guesses.  
**Verify:** Attribute, fluent, and convention-only fixtures assert documented behavior.

### T-084 — Implement cache client and read/write provider
**Depends on:** T-073, T-059, T-076  
**Work:** Recognize configured distributed-cache and Redis client calls, classify read/write operations, and extract key expressions.  
**Done when:** Cache operation and canonical key pattern appear in edge evidence.  
**Verify:** `Get`, `Set`, and Redis fixture asserts operation kinds.

### T-085 — Implement C# cache interpolation normalizer
**Depends on:** T-060, T-076  
**Work:** Normalize interpolation, concatenation, `string.Format`, and constant segments to canonical patterns; mark zero-literal anchors unresolved.  
**Done when:** Literal segments survive and placeholder names remain metadata only.  
**Verify:** Golden cases cover interpolation, concatenation, format strings, and fully dynamic keys.

### T-086 — Implement configuration-read provider
**Depends on:** T-073, T-076  
**Work:** Recognize `IConfiguration` indexer/GetValue/binding reads and environment variable reads, then normalize logical key paths.  
**Done when:** Every read edge includes key path and access form.  
**Verify:** Configuration fixture asserts indexer, section, and environment patterns.

### T-087 — Implement configuration-definition provider
**Depends on:** T-086  
**Work:** Parse supported JSON configuration files and statically visible key definitions; create config resource nodes and join known reads.  
**Done when:** Missing or environment-only values remain visible as unresolved definitions.  
**Verify:** JSON hierarchy fixture links known keys and marks absent keys.

### T-088 — Implement EF Core property, column, and migration provider
**Depends on:** T-057, T-060, T-075, T-076, T-083  
**Work:** Recognize statically mapped EF Core properties through `[Column]`, fluent `Property(...).HasColumnName(...)`, and explicit migrations such as add/alter/rename column; create column resource nodes and property-to-column/migration-to-column edges.  
**Done when:** A selected column can traverse to mapped entity properties and their semantic references, producing a genuine column-level blast radius rather than only a table-level list.  
**Verify:** Attribute, fluent, renamed-column, and convention fixtures assert the complete column→property→reference path; raw or dynamically constructed SQL is reported as unresolved rather than guessed.

### T-089 — Publish C# provider coverage report
**Depends on:** T-077, T-087, T-088  
**Work:** Generate a repository-specific report of detected patterns, enabled providers, unresolved dynamic sites, column-analysis coverage, and known blind spots.  
**Done when:** Users can judge graph coverage without reading source.  
**Verify:** Fixture snapshot includes every supported and unsupported category.

## Phase 6 — Incremental graph lifecycle and deterministic architecture rules

### T-090 — Implement full-scan orchestration
**Depends on:** T-039, T-050, T-074, T-089  
**Work:** Execute inventory, semantic extraction, provider joins, rule evaluation, metrics, and graph commit in dependency order.  
**Done when:** `archy scan` produces one complete revision with a summarized run result.  
**Verify:** Full C# fixture scan matches a committed golden graph.

### T-091 — Implement incremental scan planner
**Depends on:** T-050, T-090  
**Work:** Determine which files, symbols, joins, provider sites, and direct dependents must be recomputed after a change.  
**Done when:** Planner is conservative for correctness and avoids whole-repository reparses when safe.  
**Verify:** One-file edit fixture asserts affected work set and final graph equality with full scan.

### T-092 — Implement deletion, rename, and move reconciliation
**Depends on:** T-091, T-032, T-033  
**Work:** Close active facts for removed paths, preserve historical identities, and remap moved files when Git and content evidence permit.  
**Done when:** No active edges target a deleted node.  
**Verify:** Rename/delete scan fixture passes foreign-key and active-edge assertions.

### T-093 — Implement Git provenance capture
**Depends on:** T-090  
**Work:** Record HEAD commit SHA, dirty state, and optional file-level blame metadata without requiring a clean tree.  
**Done when:** Summary versions and replay events link to the best available repository state.  
**Verify:** Clean and dirty repository fixtures record expected provenance.

### T-094 — Implement filesystem watcher and debounce controller
**Depends on:** T-091, T-024  
**Work:** Watch in-scope paths, coalesce atomic-save bursts, honor session idle windows, and queue one incremental run at a time.  
**Done when:** Rapid edits trigger one settled analysis without lost final content.  
**Verify:** Burst-write test asserts one final graph revision.

### T-095 — Define architecture-rule schema
**Depends on:** T-020, T-002  
**Work:** Define layers, path/namespace membership, allowed directions, forbidden references, rule severity, and versioned exception references.  
**Done when:** Rules can be validated before any scan and have human-readable errors.  
**Verify:** Invalid-direction and malformed-rule fixtures fail validation.

### T-096 — Implement layer membership resolver
**Depends on:** T-095, T-032  
**Work:** Assign nodes to layers using deterministic path, namespace, and explicit overrides with conflict diagnostics.  
**Done when:** Every in-scope code node is assigned, unassigned, or ambiguous explicitly.  
**Verify:** Overlapping-rule fixture reports precedence and ambiguity correctly.

### T-097 — Implement forbidden-reference rule evaluation
**Depends on:** T-033, T-063, T-096  
**Work:** Evaluate active source/provider edges against layer direction and forbidden-reference rules, preserving exact edge evidence.  
**Done when:** Each violation identifies source, target, rule, edge kind, and remediation direction.  
**Verify:** Layer fixture asserts expected pass/fail violations.

### T-098 — Implement cycle detection
**Depends on:** T-033, T-038, T-063  
**Work:** Compute strongly connected components over configured edge kinds and expose the minimal explanatory cycle path.  
**Done when:** Cycles are deterministic regardless of database row order.  
**Verify:** Multi-node cycle fixture asserts component members and path.

### T-099 — Implement baseline-aware violation comparison
**Depends on:** T-097, T-098, T-039  
**Work:** Compare current deterministic violations with the accepted baseline, classify introduced/resolved/legacy findings, and block introduced findings by default.  
**Done when:** Existing debt is visible but a new unrelated change does not fail solely for historic violations.  
**Verify:** Legacy-plus-new fixture asserts each classification.

### T-100 — Implement `archy verify` command
**Depends on:** T-097, T-099  
**Work:** Scan required changes, evaluate deterministic rules, print concise human output plus JSON/SARIF, and return nonzero for introduced blocking violations.  
**Done when:** It is the one canonical local/CI enforcement command.  
**Verify:** Passing, legacy-only, and introduced-violation fixtures assert exit code and reports.

### T-101 — Implement architecture-exception decision model
**Depends on:** T-036, T-099  
**Work:** Allow narrow, rule-targeted accepted exceptions with author, reason, expiry/review date, and no broad suppression switch.  
**Done when:** Every exception is visible in graph explanations and health calculations.  
**Verify:** Expired and active exception fixtures produce opposite verification outcomes.

### T-102 — Implement local Git hook installer
**Depends on:** T-100  
**Work:** Install an idempotent pre-commit/pre-push wrapper that runs `archy verify` against the staged/target diff and preserves existing hook chains.  
**Done when:** Users get deterministic local enforcement without overwriting their hooks.  
**Verify:** Temporary Git repo test validates install, block, allow, and uninstall paths.

### T-103 — Publish provider-neutral CI integration
**Depends on:** T-100  
**Work:** Document and package a shell-level CI entrypoint that downloads/runs the correct Archy binary and emits machine-readable results.  
**Done when:** Any CI system can make `archy verify` a required status check.  
**Verify:** Containerized CI fixture blocks an introduced layer violation.

### T-104 — Publish GitHub Actions integration
**Depends on:** T-103  
**Work:** Provide a maintained GitHub Action/workflow template with artifact upload, PR annotations, baseline handling, and branch-protection instructions.  
**Done when:** GitHub users can configure a required `archy/verify` check.  
**Verify:** Action integration test runs against a fixture pull request workflow.

### T-105 — Implement post-tool changed-path resolution
**Depends on:** T-050, T-094, T-100  
**Work:** Resolve in-scope source paths changed by `Edit`, `Write`, or `Bash` tool use from hook input plus a bounded pre/post workspace snapshot or Git diff; distinguish code edits from generated/binary/no-op changes.  
**Done when:** The post-edit validation path covers shell-originated source writes without scanning unrelated files, while external-editor changes continue through watcher plus commit/CI gates.  
**Verify:** Hook fixtures using patch-style edits and shell writes yield identical changed-path sets and one incremental validation run.

### T-106 — Create first-class summary-batch persistence
**Depends on:** T-035, T-036  
**Work:** Add immutable `summary_batches` and `summary_batch_members` records containing session ID, settle reason, member ordering, co-touched relationships, request state, model request metadata, and resulting summary-version links.  
**Done when:** `touched_together` is stored as durable provenance rather than inferred later from timestamps.  
**Verify:** Multi-file session fixture persists one ordered batch and round-trips its member/co-touch graph exactly.

## Phase 7 — AI-backed memory, versions, and staleness

### T-110 — Define model-provider contract
**Depends on:** T-000, T-006  
**Work:** Define summary-batch and embedding interfaces, request/response schemas, usage metadata, cancellation, retry taxonomy, and a deterministic fake provider.  
**Done when:** Core memory/duplicate code depends only on the abstraction.  
**Verify:** Contract tests run against the fake provider.

### T-111 — Implement OpenAI model provider
**Depends on:** T-110, T-025, T-013  
**Work:** Implement user-key-authenticated OpenAI requests for structured summary generation and embeddings, configurable model IDs, bounded retries, and rate-limit handling.  
**Done when:** Requests are schema-validated, redacted in logs, and never use Codex transcript scraping.  
**Verify:** Mock HTTP tests cover success, invalid schema, timeout, rate limit, and cancellation.

### T-112 — Implement important-node eligibility policy
**Depends on:** T-032, T-034  
**Work:** Select directories, namespaces, public types, public APIs, and configured modules; exclude trivial/generated nodes unless explicitly included.  
**Done when:** Eligibility is deterministic and explainable per node.  
**Verify:** Fixture asserts expected included and excluded nodes.

### T-113 — Implement public-surface diff engine
**Depends on:** T-034  
**Work:** Compare normalized interface fingerprints between revisions and emit structured additions/removals/changes.  
**Done when:** Summary prompting receives facts rather than inferring API changes.  
**Verify:** Public/private/body-only change fixtures assert exact diff categories.

### T-114 — Implement touched-node session accumulator
**Depends on:** T-036, T-094, T-106, T-112  
**Work:** Group important nodes touched since the last settle point by Archy/Codex session, with idle debounce and explicit end-session flush.  
**Done when:** Related edits create one persisted batch candidate set with explicit co-touched membership.  
**Verify:** Multi-file burst fixture produces one first-class session batch.

### T-115 — Implement summary prompt construction
**Depends on:** T-113, T-114, T-006  
**Work:** Build bounded prompts from source excerpts, prior summary, deterministic public diff, relevant edges, and co-touched context; request one JSON object per node.  
**Done when:** Prompts exclude irrelevant full-repository source and adhere to data policy.  
**Verify:** Snapshot tests assert prompt contents and redaction.

### T-116 — Implement structured summary response validation
**Depends on:** T-110, T-115  
**Work:** Validate node IDs, JSON shape, length, text quality constraints, duplicate entries, and model refusal/error handling before persistence.  
**Done when:** Invalid response leaves existing summaries intact and reports retryable/degraded state.  
**Verify:** Malformed-response fixtures assert no accidental writes.

### T-117 — Persist immutable summary versions
**Depends on:** T-035, T-116, T-093  
**Work:** Insert summary/diff versions with source revision, commit, model metadata, creation time, and originating summary batch; supersede only by link.  
**Done when:** Historical summaries support replay and provenance.  
**Verify:** Two-version fixture asserts immutable prior text.

### T-118 — Implement lazy staleness propagation
**Depends on:** T-033, T-113, T-117  
**Work:** When public surface changes, mark direct dependents stale; when it does not, update hash without cascading.  
**Done when:** Staleness never triggers eager downstream model calls.  
**Verify:** Dependency-chain fixture asserts direct-only stale state and zero extra requests.

### T-119 — Implement on-view/on-edit stale regeneration
**Depends on:** T-118, T-115  
**Work:** Queue regeneration only when a stale node is opened in UI/MCP or edited, while coalescing duplicate requests.  
**Done when:** Stale summaries become current on demand.  
**Verify:** View and edit fixtures invoke exactly one new batch.

### T-120 — Implement directory-summary rollups
**Depends on:** T-112, T-117  
**Work:** Synthesize directory summaries exclusively from current child summaries and structural metadata, never a directory-wide raw-code reread.  
**Done when:** Rollup provenance lists included child summary versions.  
**Verify:** Change child summary fixture updates rollup input set deterministically.

### T-121 — Implement model failure queue and user controls
**Depends on:** T-111, T-116  
**Work:** Persist retryable summary/embedding work, expose retry/pause/disable-AI commands, and keep deterministic scanning available when AI is disabled.  
**Done when:** AI outage degrades memory only, not graph or verification.  
**Verify:** Offline-provider test completes scan and marks requested summaries pending.

### T-123 — Implement per-repository AI consent and source-sharing controls
**Depends on:** T-020, T-025, T-006  
**Work:** Require explicit repository-level enablement before source-derived summary or embedding requests leave the machine; support disabled, summaries-only, and summaries-plus-embeddings modes.  
**Done when:** Supplying an API key alone never silently opts a repository into outbound source sharing.  
**Verify:** Fresh-workspace and mode-matrix fixtures assert no unexpected outbound request.

### T-124 — Implement model prompt-injection isolation
**Depends on:** T-111, T-115, T-116  
**Work:** Treat all repository text as untrusted data, isolate it from prompt instructions with structured delimiters, prohibit tool execution in the model workflow, and validate output strictly against the requested schema.  
**Done when:** Source comments attempting to override instructions cannot alter request policy, cause tool actions, or produce unvalidated state changes.  
**Verify:** Adversarial-source fixtures prove prompt boundaries and schema rejection behavior.

### T-125 — Implement AI budget, rate, and concurrency governor
**Depends on:** T-111, T-121  
**Work:** Add repository-configurable token/cost ceilings, per-run batch limits, request concurrency, cooldowns, model defaults, and explicit quota-exhausted states.  
**Done when:** Large repositories cannot create unbounded model spend or request storms.  
**Verify:** Simulated usage/rate-limit fixtures assert queuing, pausing, and no over-budget request.

## Phase 8 — Duplicate detection and durable calibration

### T-130 — Define sidecar protocol and capability handshake
**Depends on:** T-004, T-005  
**Work:** Define line-delimited JSON request/response envelopes, tool versions, timeouts, cancellation, allowed paths, and capability probes for Node/Python sidecars.  
**Done when:** The host rejects unknown/incompatible sidecar versions before analysis.  
**Verify:** Protocol tests cover handshake mismatch and malformed message.

### T-131 — Package the jscpd clone-detection sidecar
**Depends on:** T-130  
**Work:** Package/pin jscpd in a Node sidecar that accepts a file set and returns clone occurrences with language, ranges, token metrics, and tool version.  
**Done when:** The host does not execute jscpd through interpolated shell commands.  
**Verify:** Sidecar integration test analyzes C# fixtures and validates allowed-path enforcement.

### T-132 — Map structural clone occurrences to symbols
**Depends on:** T-131, T-057  
**Work:** Intersect jscpd ranges with method/function nodes, aggregate clone evidence per candidate pair, and discard non-code/generated matches.  
**Done when:** Structural findings identify user-facing symbols and exact evidence ranges.  
**Verify:** Clone fixture produces stable pair IDs and range links.

### T-133 — Implement interface-signature similarity signal
**Depends on:** T-034  
**Work:** Compare parameter types, return type, generic arity, and tokenized name edit distance; preserve component scores.  
**Done when:** Signature signal is independent from clone/embedding signals.  
**Verify:** Similar and dissimilar signature fixture asserts scores and explanations.

### T-134 — Implement semantic embedding chunk selection
**Depends on:** T-061, T-112  
**Work:** Build one embedding chunk per eligible method from signature, documentation, and body after stripping imports/class boilerplate.  
**Done when:** Whole-file boilerplate is never embedded as logic evidence.  
**Verify:** Chunk snapshots exclude imports and enclosing type declarations.

### T-135 — Implement embedding cache and invalidation
**Depends on:** T-110, T-134, T-032  
**Work:** Persist vector, model ID, content hash, method ID, and creation revision; recompute only when chunk content/model changes.  
**Done when:** Embedding calls are deduplicated across unchanged scans.  
**Verify:** Repeated-scan test makes zero second-run embedding calls.

### T-136 — Implement complexity-band comparison
**Depends on:** T-061, T-135  
**Work:** Partition candidates by language and complexity/LOC bands; exclude data-only DTO/record methods from duplicate-logic matching.  
**Done when:** Short snippets are not compared with complex logic under a flat threshold.  
**Verify:** DTO and mixed-complexity fixtures assert exclusion/banding.

### T-137 — Implement relative embedding similarity
**Depends on:** T-136  
**Work:** Compute cosine similarity and corpus-relative statistics per band; store z-score and population size alongside raw similarity.  
**Done when:** The explanation never reports only an uncalibrated absolute threshold.  
**Verify:** Synthetic-vector test asserts expected ranking and z-score.

### T-138 — Implement two-of-three duplicate aggregator
**Depends on:** T-132, T-133, T-137  
**Work:** Create a likely-duplicate finding only when at least two independent signals qualify; include all signal evidence and a confidence/rationale.  
**Done when:** One strong signal alone is advisory evidence but not a duplicate finding.  
**Verify:** Truth-table tests cover every signal combination.

### T-139 — Implement data-shape naming checker
**Depends on:** T-136  
**Work:** Route record/DTO similarity to a separate naming/convention advisory rather than clone logic detection.  
**Done when:** Users do not receive false duplicate-logic alerts for intentionally similar data shapes.  
**Verify:** DTO fixture emits only the naming advisory.

### T-140 — Implement duplicate decision feedback
**Depends on:** T-036, T-138  
**Work:** Record accepted/ignored/modified resolutions per comparable pair/pattern, suppress repeated ignored findings narrowly, and adjust confidence calibration with auditable evidence.  
**Done when:** Calibration never silently changes global thresholds without decision provenance.  
**Verify:** Repeated ignored-pair fixture becomes quieter while a different pair remains visible.

### T-141 — Implement duplicate inspection query surface
**Depends on:** T-138, T-140  
**Work:** Return side-by-side code references, each signal, corpus context, decisions, and suggested action to MCP/API/UI consumers.  
**Done when:** A human can reproduce why Archy flagged a pair.  
**Verify:** API golden response contains all signal evidence and no raw embedding vector.

### T-142 — Create duplicate-detection evaluation corpus
**Depends on:** T-017, T-138  
**Work:** Curate labelled C# examples for true clones, semantic duplicates, unrelated structural similarity, DTOs, generated code, and historically ignored/accepted findings; define precision, recall, and false-positive budgets.  
**Done when:** Threshold and calibration changes are evaluated against a stable corpus instead of anecdotal examples.  
**Verify:** CI emits a versioned quality report and fails agreed regressions.

### T-143 — Implement bounded embedding candidate retrieval
**Depends on:** T-135, T-136, T-142  
**Work:** Build a revision-aware nearest-neighbor candidate index or bounded retrieval strategy per language/complexity band before relative-similarity scoring.  
**Done when:** Duplicate detection avoids all-pairs corpus comparisons while preserving documented recall on the evaluation corpus.  
**Verify:** Scale benchmark proves bounded comparisons and candidate-recall fixture meets the accepted target.

### T-144 — Implement duplicate-finding lifecycle reconciliation
**Depends on:** T-039, T-138, T-140  
**Work:** Close, supersede, or retain duplicate findings as candidate symbols change, disappear, or resolve; retain decision provenance without displaying obsolete active alerts.  
**Done when:** The UI and health score include only findings active at their selected graph revision.  
**Verify:** Edit/delete fixture closes the correct finding while preserving its historical record.

## Phase 9 — Cohesion, placement, secondary signals, and health

### T-150 — Package the Louvain sidecar
**Depends on:** T-130  
**Work:** Package a pinned Node `graphology` or Python `networkx` worker behind the shared sidecar protocol; choose one implementation and document its deterministic seed/ordering behavior.  
**Done when:** The host sends a weighted import graph and receives stable cluster assignments.  
**Verify:** Reordered-input graph returns identical clusters.

### T-151 — Implement module cohesion metrics
**Depends on:** T-033, T-150  
**Work:** Materialize the relevant graph, compute communities plus internal/external edge ratios, and store revisioned cluster membership.  
**Done when:** Cohesion values identify their graph scope and calculation inputs.  
**Verify:** Fixture with two connected modules reports expected relative cohesion.

### T-152 — Implement placement dependency-overlap scorer
**Depends on:** T-151  
**Work:** Score a proposed/new symbol dependency set against module clusters using weighted overlap and confidence-aware edges.  
**Done when:** Results include top candidates, score components, and abstain when evidence is weak.  
**Verify:** Ambiguous-placement fixture returns an abstention instead of a forced recommendation.

### T-153 — Implement sibling naming convention miner
**Depends on:** T-032  
**Work:** Infer file/type naming patterns from siblings in a candidate module and produce safe name suggestions.  
**Done when:** It never invents a preferred convention when samples conflict.  
**Verify:** Consistent and mixed naming fixtures assert suggestion/abstention.

### T-154 — Implement split-vs-append heuristic
**Depends on:** T-151, T-153  
**Work:** Combine module size, cohesion, fan-in/out, and sibling conventions into an explained append/new-file advisory.  
**Done when:** Thresholds are config-driven and recommendation is never a hard block.  
**Verify:** Small-cohesive and large-low-cohesion fixtures assert opposite advice.

### T-155 — Implement placement decision feedback
**Depends on:** T-036, T-152, T-154  
**Work:** Store accepted/ignored/modified placement outcomes and calibrate only matching contexts.  
**Done when:** A user's decision can be surfaced later as evidence.  
**Verify:** Decision fixture affects only the intended module context.

### T-156 — Implement Git co-change mining
**Depends on:** T-093  
**Work:** Mine local Git history for files changed together, score temporal coupling, and store it as a distinct non-structural edge type.  
**Done when:** Co-change edges never masquerade as semantic dependencies.  
**Verify:** Seeded Git-history fixture asserts edge kind and score.

### T-157 — Implement documentation-debt metric
**Depends on:** T-118, T-093  
**Work:** Count nodes stale across configurable change/revision thresholds and expose age/distribution metrics.  
**Done when:** Debt calculations exclude ineligible and AI-disabled nodes correctly.  
**Verify:** Revision-age fixture asserts metric totals.

### T-158 — Implement architecture-health calculation
**Depends on:** T-037, T-099, T-140, T-157  
**Work:** Calculate a versioned weighted score from introduced/legacy violations, unresolved duplicate findings, staleness/debt, and decision resolution quality; expose all components.  
**Done when:** The score is explainable, configurable, and cannot hide a blocking violation behind a high aggregate.  
**Verify:** Component-isolation tests assert score deltas and explanations.

### T-159 — Implement English change/changelog generation
**Depends on:** T-115, T-117  
**Work:** Persist the per-node English diff sentence from the summary batch and compose session-level change notes from them.  
**Done when:** Changelog text links back to underlying node summary versions.  
**Verify:** Multi-node session fixture produces a deterministic ordered changelog.

### T-160 — Implement unified advisory composition service
**Depends on:** T-141, T-144, T-152, T-154, T-155  
**Work:** Compose bounded duplicate, placement, split/cohesion, and coverage advisories for a changed or proposed symbol; include confidence, independent signals, prior decisions, and explicit abstention when evidence is weak.  
**Done when:** Every advisory surface receives one consistent, explainable payload and deterministic violations remain separate from recommendations.  
**Verify:** Composition fixtures assert signal ranking, decision suppression, confidence explanation, and abstention behavior.

## Phase 10 — MCP, Codex integration, generated guidance, and hook behavior

### T-170 — Select and prove the AOT-safe MCP implementation
**Depends on:** T-013, T-004  
**Work:** Evaluate the .NET MCP SDK against Native AOT or implement the required MCP protocol surface directly; record the proof in an ADR.  
**Done when:** A Native AOT binary passes MCP initialization and tool-list conformance tests.  
**Verify:** Test client connects to the published `osx-arm64` binary over stdio.

### T-171 — Implement MCP server lifecycle and instructions
**Depends on:** T-170, T-016  
**Work:** Implement stdio server startup, workspace selection, cancellation, error mapping, and concise server instructions describing required preflight/verification workflow.  
**Done when:** Tool calls fail safely when Archy is initializing or degraded.  
**Verify:** Lifecycle tests cover normal startup, no workspace, lock contention, and shutdown.

### T-172 — Implement `get_dependents` MCP tool
**Depends on:** T-038, T-171  
**Work:** Accept a file/symbol and depth/options, return direct/transitive dependents with edge explanations and truncation metadata.  
**Done when:** Results identify active graph revision and confidence.  
**Verify:** Graph traversal fixture asserts direct and transitive output.

### T-173 — Implement `check_violation` MCP tool
**Depends on:** T-100, T-171  
**Work:** Accept changed paths or a proposed-change descriptor, run deterministic checks against the relevant revision, and return introduced/legacy results plus remediation.  
**Done when:** It provides preflight guidance but makes no false claim to physically block writes.  
**Verify:** Violating and clean proposal fixtures return documented structured results.

### T-174 — Implement `get_module_rules` MCP tool
**Depends on:** T-095, T-171  
**Work:** Return resolved layer, applicable rules, exceptions, local conventions, and current health for a path/module.  
**Done when:** Agents can query exact local constraints without a full AGENTS snapshot.  
**Verify:** Nested-module fixture asserts precedence and exception visibility.

### T-175 — Implement `check_duplicate` MCP tool
**Depends on:** T-141, T-171  
**Work:** Accept a symbol/signature/content reference, return existing candidates, signal evidence, prior decisions, and an abstention state.  
**Done when:** It exposes confidence and reasons rather than only a binary answer.  
**Verify:** Duplicate and no-evidence fixtures assert outputs.

### T-175A — Implement `suggest_placement` MCP tool
**Depends on:** T-052, T-152, T-154, T-160, T-171  
**Work:** Accept a proposed path plus snippet or declared dependency set without writing it to the repository; derive an ephemeral dependency profile and return ranked module/file placement, split-vs-append advice, confidence, evidence, and abstention.  
**Done when:** Codex can ask where new code belongs before creating a file, and the tool never mutates the working tree to analyze a proposal.  
**Verify:** Proposed-code fixtures assert strong placement, weak-evidence abstention, and unchanged Git status.

### T-176 — Implement MCP tools for decisions and session control
**Depends on:** T-036, T-171  
**Work:** Add tools to record a decision, retrieve decisions for a node/edge, start/end a session, and request summary flush.  
**Done when:** Tool operations validate actor, targets, and allowed resolution states.  
**Verify:** MCP contract tests persist and retrieve one decision/session.

### T-177 — Implement Streamable HTTP MCP transport
**Depends on:** T-170  
**Work:** Add optional local HTTP transport with loopback binding, explicit authentication configuration, request limits, and the same tool contracts as stdio.  
**Done when:** It is disabled by default and does not expose a network listener accidentally.  
**Verify:** Transport parity tests run every MCP tool over stdio and HTTP.

### T-178 — Build the Codex plugin package
**Depends on:** T-171, T-004  
**Work:** Package MCP configuration, hook definitions, installer guidance, and platform-specific Archy command resolution in a Codex plugin without bundling secrets.  
**Done when:** Plugin install enables the local Archy integration after required trust/restart steps.  
**Verify:** Fresh-profile integration test installs and lists the MCP server/hooks.

### T-179 — Implement SessionStart guidance hook
**Depends on:** T-171, T-176  
**Work:** Call Archy at session start and return bounded current architecture/rules/open-decision context as extra developer context.  
**Done when:** Output is concise, redacted, and degrades safely if Archy is unavailable.  
**Verify:** Hook JSON snapshot validates context and unavailable behavior.

### T-180 — Implement PostToolUse edit-review hook
**Depends on:** T-100, T-105, T-160, T-171  
**Work:** Match `Bash|Edit|Write`, resolve changed paths, run incremental deterministic validation, and compose confidence-scored duplicate/placement/cohesion advisories; emit `continue: false` with remediation only for introduced blocking violations.  
**Done when:** Shell-originated edits receive the same analysis as patch-style edits, while documentation and UI call this a post-edit turn stop, never a pre-write block.  
**Verify:** Codex hook fixtures assert equivalent advisory/turn-stop JSON for patch-style and shell-originated cycle/layer violations.

### T-181 — Implement hook event recording and UI publication
**Depends on:** T-036, T-180  
**Work:** Persist edit allowed/advisory/turn-stopped events with graph revision, advisory payload, and changed-path evidence; send them to the WebSocket event stream.  
**Done when:** Replay and live UI use the same event record.  
**Verify:** Hook fixture produces ordered database and WebSocket events.

### T-182 — Implement safe `AGENTS.md` managed-section generation
**Depends on:** T-090, T-095, T-101  
**Work:** Regenerate only `<!-- archy:begin -->`/`<!-- archy:end -->` content containing architecture snapshot, graph-friendly conventions, exceptions, and next-session note.  
**Done when:** User-authored guidance is byte-for-byte preserved and output is idempotent/size-bounded.  
**Verify:** Existing AGENTS fixture survives repeated regeneration unchanged outside markers.

### T-183 — Publish enforcement semantics and setup guide
**Depends on:** T-102, T-104, T-180  
**Work:** Document advisory, post-edit turn-stop, local commit gate, CI gate, bypass limitations, and required branch-protection configuration.  
**Done when:** No public document claims Archy can prevent arbitrary local writes.  
**Verify:** Documentation review checklist validates all enforcement claims.

### T-184 — Implement Stop session-finalization hook
**Depends on:** T-114, T-121, T-171  
**Work:** On the Codex `Stop` lifecycle event, record the session end, settle pending file changes, queue one final batched summary request, and return a concise failure/deferred-status message when work cannot complete.  
**Done when:** Ending a Codex task is an explicit summary settle point in addition to the idle debounce, and a model outage never blocks task completion.  
**Verify:** Hook integration fixture edits multiple eligible nodes, ends the task, and asserts one final batch plus a persisted session end event.

### T-185 — Implement post-revision AGENTS snapshot publication
**Depends on:** T-090, T-182  
**Work:** After each successfully committed graph revision, schedule idempotent regeneration of the Archy-managed `AGENTS.md` section; prevent the generated-file write from recursively triggering a new source analysis run.  
**Done when:** Every successful analysis has a matching current architecture snapshot, while user-owned content and watcher stability are preserved.  
**Verify:** Repeated scan fixture updates the managed block once, preserves surrounding bytes, and produces no follow-up analysis loop.

## Phase 11 — Native API, event stream, and SPA

### T-190 — Implement AOT-safe local web host
**Depends on:** T-014, T-013  
**Work:** Serve versioned REST endpoints, static SPA assets, and loopback-only defaults from the Native AOT host.  
**Done when:** UI starts with one Archy command and no public interface exposure by default.  
**Verify:** AOT host serves health/API/static smoke tests on loopback.

### T-191 — Implement graph-query REST API
**Depends on:** T-038, T-190  
**Work:** Expose paged nodes/edges, neighborhood traversal, edge provenance, summaries, decisions, duplicates, clusters, metrics, and revision selectors.  
**Done when:** APIs are revision-aware, schema-versioned, and avoid unbounded graph payloads.  
**Verify:** OpenAPI/contract tests cover validation, pagination, and errors.

### T-192 — Implement WebSocket event stream
**Depends on:** T-181, T-190  
**Work:** Publish graph revision, scan progress, summary state, rule finding, hook result, decision, and health events with sequence numbers and reconnect catch-up.  
**Done when:** Clients can recover from a disconnect without duplicate/missing events.  
**Verify:** Disconnect/reconnect test reconstructs ordered event sequence.

### T-193 — Scaffold the SPA and API client
**Depends on:** T-191, T-192  
**Work:** Create a typed React/TypeScript SPA, generated/validated API client, route structure, local error boundary, and accessible base layout.  
**Done when:** It loads an empty/degraded/ready workspace with clear states.  
**Verify:** Component tests cover each startup state.

### T-194 — Implement graph renderer
**Depends on:** T-193  
**Work:** Use Cytoscape.js or equivalent to render paged/neighborhood graph data with stable node IDs, layout controls, and performance-safe expansion.  
**Done when:** Large graphs are not fetched/rendered as one uncontrolled force layout.  
**Verify:** Browser performance test meets initial-load budget on benchmark fixture.

### T-195 — Implement graph visual encoding
**Depends on:** T-194, T-158  
**Work:** Encode node kind/fan-in/fan-out/staleness and edge provider/kind/confidence with legend, non-color cues, and filters.  
**Done when:** Every color/style has accessible textual meaning.  
**Verify:** Visual regression and accessibility tests cover legend/filter behavior.

### T-196 — Implement node and edge inspector
**Depends on:** T-191, T-195  
**Work:** Show node versions, summaries, source links, direct neighbors, fingerprint changes, and edge provenance/join evidence/decisions.  
**Done when:** "Why does X depend on Y?" is answered from stored evidence and summaries, with uncertainty disclosed.  
**Verify:** Inspector fixture asserts source/provenance/decision panels.

### T-197 — Implement staleness and documentation-health views
**Depends on:** T-117, T-118, T-157, T-195  
**Work:** Render current, stale, and missing summary states; show stale reason, age, regeneration action, and documentation-debt totals.  
**Done when:** Users can distinguish changed code from stale dependent summaries.  
**Verify:** Staleness fixture drives all states in UI tests.

### T-198 — Implement live enforcement overlay
**Depends on:** T-181, T-192, T-195  
**Work:** Animate allowed/advisory/turn-stopped events, show confidence/reasons, and link deterministic violations to remediation details.  
**Done when:** Events remain inspectable after animation completes.  
**Verify:** WebSocket fixture produces expected overlay and event history.

### T-199 — Implement blast-radius view
**Depends on:** T-038, T-194  
**Work:** Visualize transitive dependents for a selected node/change with depth, edge-kind, and confidence controls.  
**Done when:** Traversal truncation and cycles are communicated clearly.  
**Verify:** Dependency-chain/cycle UI tests assert node sets and labels.

### T-200 — Implement session replay timeline
**Depends on:** T-036, T-117, T-181, T-192  
**Work:** Provide ordered session timeline, revision-aware slider, play/pause/seek, and event-state reconstruction without mutating current graph.  
**Done when:** Replay shows touches, checks, decisions, staleness, and summary versions in actual order.  
**Verify:** Seeded session replay UI test matches known event frames.

### T-201 — Implement duplicate and placement workbenches
**Depends on:** T-141, T-154, T-155, T-193  
**Work:** Show duplicate pairs side-by-side and placement/split advice with evidence, decision controls, and abstention state.  
**Done when:** Users can accept/ignore/modify without treating advisory signals as rules.  
**Verify:** UI decision tests persist correct resolution and refresh confidence.

### T-202 — Implement health dashboard
**Depends on:** T-158, T-193  
**Work:** Display health score, component deltas, introduced/legacy violations, duplicate status, stale-summary debt, and recent decisions.  
**Done when:** Aggregate score links to the exact contributing facts.  
**Verify:** Component fixture verifies dashboard values and drill-downs.

### T-203 — Implement deterministic natural-language query mapping
**Depends on:** T-191  
**Work:** Map a small documented query grammar (for example "what breaks if I delete …" or "what uses this column?") to graph traversals; return clarification for unsupported phrasing rather than inventing an LLM answer.  
**Done when:** Queries are explainable and run against a declared graph revision.  
**Verify:** Query corpus tests assert traversal mapping and unsupported responses.

### T-204 — Implement UI settings and capability diagnostics
**Depends on:** T-016, T-193  
**Work:** Surface provider/LSP/sidecar/model readiness, configuration validation, coverage limitations, and safe remediation links.  
**Done when:** A degraded graph cannot look fully healthy.  
**Verify:** Disabled-sidecar and unavailable-model UI tests assert warnings.

## Phase 12 — Open-source packaging, security, quality, and release

### T-210 — Implement macOS distribution packaging
**Depends on:** T-012, T-178  
**Work:** Package AOT binaries, plugin assets, sidecar manifests, checksums, install/uninstall scripts, and architecture-specific release archives.  
**Done when:** Installation does not require a globally installed .NET runtime and detects missing sidecar prerequisites clearly.  
**Verify:** Fresh macOS VM install/uninstall smoke tests for arm64 and x64.

### T-211 — Implement sidecar acquisition and integrity checks
**Depends on:** T-130, T-131, T-150  
**Work:** Resolve supported Node/Python runtime/sidecar assets, pin versions, verify checksums, and expose offline/manual-install paths.  
**Done when:** Sidecar upgrades are deliberate and auditable.  
**Verify:** Tampered sidecar fixture is rejected before execution.

### T-212 — Implement safe update and migration workflow
**Depends on:** T-030, T-210  
**Work:** Check product/schema/sidecar compatibility before upgrade, create backup, migrate atomically, and offer rollback guidance.  
**Done when:** An incompatible release never silently opens/rewrites state.  
**Verify:** Upgrade simulation from previous release fixture passes/blocks appropriately.

### T-213 — Add supply-chain and dependency security checks
**Depends on:** T-011, T-211  
**Work:** Scan .NET/Node/Python dependencies, lock files, licenses, and release artifacts; define remediation policy for vulnerable sidecars.  
**Done when:** CI fails on policy-violating dependency findings.  
**Verify:** Controlled vulnerable-dependency fixture triggers the gate.

### T-214 — Add local API security controls
**Depends on:** T-177, T-190  
**Work:** Enforce loopback default, explicit remote opt-in, authentication for HTTP MCP, CORS policy, request-size/time limits, and safe error serialization.  
**Done when:** A default install cannot be reached from another network host.  
**Verify:** Network/security integration tests assert bind and auth behavior.

### T-215 — Add performance and scale benchmark suite
**Depends on:** T-007, T-090, T-194  
**Work:** Benchmark representative .NET repository sizes for cold/incremental scan, verify, hook, API, UI, model batching, and SQLite traversal.  
**Done when:** Release CI detects material regressions against approved budgets.  
**Verify:** Benchmark baseline and regression fixture enforce thresholds.

### T-216 — Add resilience and recovery test suite
**Depends on:** T-005, T-121, T-192  
**Work:** Test process crash, LSP restart, sidecar timeout, model outage, disk-full simulation, lock contention, and WebSocket reconnection.  
**Done when:** Each scenario preserves a usable prior graph and actionable diagnostic.  
**Verify:** Automated fault-injection suite passes.

### T-217 — Add accessibility and UX verification
**Depends on:** T-193, T-204  
**Work:** Test keyboard navigation, focus order, contrast, reduced motion, graph alternatives, screen-reader labels, and error recovery.  
**Done when:** Graph information is not available only through color or animation.  
**Verify:** Automated accessibility checks plus manual acceptance checklist pass.

### T-218 — Write the user documentation set
**Depends on:** T-183, T-210  
**Work:** Publish installation, quickstart, configuration, C# coverage, AI/privacy, sidecars, enforcement/CI, MCP/plugin, troubleshooting, and limitations documentation.  
**Done when:** Documentation accurately distinguishes guarantees from advisories and records all static-analysis blind spots.  
**Verify:** Docs test validates commands/snippets against release artifacts.

### T-219 — Write the contributor and extension documentation
**Depends on:** T-070, T-130, T-170  
**Work:** Document provider contracts, pattern-table authoring, language-adapter requirements, sidecar protocol, migration rules, test fixtures, and AOT gate expectations.  
**Done when:** A contributor can add a C# framework pattern without reverse-engineering the host.  
**Verify:** Follow-the-guide exercise adds a test pattern in a clean branch.

### T-220 — Establish open-source project governance
**Depends on:** T-210  
**Work:** Add license, security policy, code of conduct, contribution guide, issue templates, release process, support boundaries, and vulnerability disclosure path.  
**Done when:** The repository is ready for public issues and contributions.  
**Verify:** Repository-health checklist passes.

### T-221 — Create release acceptance checklist
**Depends on:** T-001, T-018, T-215, T-218  
**Work:** Create a sign-off checklist that maps every original feature to an acceptance scenario, performance budget, docs section, and known limitation.  
**Done when:** No release is declared complete by a feature list alone; all evidence links are required.  
**Verify:** A dry-run release fills every checklist field from CI artifacts.

---

## Enforcement behavior to implement and communicate

| Moment | Mechanism | What it guarantees | What it does not guarantee |
| --- | --- | --- | --- |
| Before an edit | `check_violation` MCP tool, SessionStart guidance, generated Archy section in `AGENTS.md` | Codex/human receives current rules and can preflight a change | The caller can still choose not to preflight |
| Immediately after a Codex edit | `PostToolUse` hook matches `Bash|Edit|Write`, resolves changed paths, and can stop the current turn with remediation | The agent does not silently continue after introducing a deterministic violation; it also receives evidence-based advisory guidance | The write has already occurred and cannot be treated as prevented |
| At Codex task end | `Stop` hook records the session and flushes one pending summary batch | Related edits settle into one durable memory update when AI is available | It does not make model availability a condition of ending the task |
| Local delivery | `archy verify` invoked by preserved Git hooks | A normal commit/push is blocked by introduced deterministic violations | A user can bypass local hooks |
| Shared delivery | Required CI status check plus protected branch | Invalid changes cannot merge into a protected branch | It cannot stop arbitrary uncommitted local experimentation |

## Explicit initial-release limitations

- The live graph is repository/monorepo-local; Archy does not assert cross-repository message or contract knowledge.
- C# is the supported source language. The provider engine is extensible, but a new language requires a tested language adapter and language-specific syntax normalization.
- Explicit static DI, static routing/key literals, and statically visible configuration are covered. Reflection/assembly scanning, no-literal dynamic routes/keys, and topology defined outside source remain visible limitations rather than guessed facts.
- Column-level blast radius covers statically mapped EF Core properties/columns and explicit EF migrations. Raw or dynamically constructed SQL is shown as unresolved rather than treated as column coverage.
- Hard enforcement applies to deterministic newly introduced violations at commit/merge boundaries. Semantic duplicates, placement, cohesion, and model summaries are explainable advisories.
- Long-term archival/compaction of immutable historical graph and memory records is deliberately deferred; the initial release keeps the required history locally.
- The initial binary release targets macOS arm64 and x64. Linux support begins only after a separate platform acceptance run.
