# Advanced architecture intelligence — implementation handoff

## Exact scope

Implement these three groups, in this order:

1. **Make similarity genuinely powerful**
   - hybrid retrieval;
   - “why not reuse?” explanation;
   - similarity clusters;
   - cross-revision reintroduction detection.
2. **Add change-planning tools**
   - `plan_change`;
   - `impact_analysis`;
   - `safe_refactor`;
   - `explain_architecture`.
3. **Make Codex integration proactive**
   - change preflight;
   - post-edit duplicate/reuse guidance;
   - session-aware context;
   - graph-delta-based PR/change summaries.
4. **Add `archy doctor` diagnostics**
   - repository/workspace/configuration readiness;
   - `archy doctor list` configured-language inventory;
   - language-server command and semantic-readiness evidence.

This document is deliberately implementation-specific. The next agent should follow it without inventing a second architecture.

## Architecture invariants

- One graph is one Git repository. Do not introduce cross-repository retrieval or persistence.
- Existing workspace SQLite is the sole durable state. Extend its migration catalog; never add an ad-hoc JSON side database.
- Feature folders are vertical slices. Keep command/query, handler, contracts, repository, adapters, and tests grouped under the owning capability.
- Existing deterministic graph facts are the only source of enforcement. All features here are advisory and must not alter `archy verify` semantics.
- Keep every model request behind `IRepositoryAiConsentPolicy`, `IEmbeddingModelProviderResolver`, `IEmbeddingCacheResolver`, and request-governor boundaries. Never write keys to files or responses.
- Native AOT rules apply: source-generated serialization, no reflection-only data binding, no dynamic assembly scanning.
- MCP tools are read-only regarding the worktree. Local cache/session/summary persistence is allowed only through existing workspace state abstractions.
- Every result must distinguish: deterministic graph fact, persisted advisory evidence, generated model evidence, and abstention/missing evidence.
- Before delivery: full tests, UI tests/build, release build, architecture-contract script, and `archy verify --path .`.

## Current reusable surfaces

| Need | Reuse first |
| --- | --- |
| Graph revision/symbol/edge history | `Features/Graph/ReadGraphRevision`, graph version tables, `GraphRevisionSnapshot` |
| Structural similarity | `Features/Queries/FindSimilar/SimilarCodeFinder` |
| Query embedding/cache/consent | `Features/Duplicates/EmbeddingCache`, `IndexEmbeddings`, `Memory/AuthorizeAiSourceSharing` |
| Duplicate signal lifecycle | `Features/Duplicates/*`, especially structural clones, interface signatures, scoring, findings |
| Rules/layers/cycles | `Features/Architecture/*` |
| Placement guidance | `Features/Placement/*` and existing `suggest_placement` MCP tool |
| Decisions/summaries/sessions | `Features/Decisions`, `Features/Memory`, `Features/Sessions` |
| Hook event stream | `Features/Integrations/Codex`, `Features/CommandLine/Integrations/CodexHooks`, `Features/Web/StreamHookEvents` |
| MCP transport | `Features/Integrations/Mcp/RunMcpServer` |

---

# Epic D — `archy doctor` and configured-language inventory

## User outcome

`archy doctor --path .` is a non-mutating operational readiness report: repository/worktree, workspace state/database, effective configuration, graph/analysis state, verification readiness, hooks/MCP, optional embeddings, and language-server readiness.

`archy doctor list --path .` lists **all effective configured language profiles**, even when a language has no matching files or a missing executable. This is mandatory so language support stays config-driven rather than C#-hardcoded.

```text
archy doctor [--path <path>] [--config <path>] [--state-root <path>] [--json]
archy doctor list [--path <path>] [--config <path>] [--state-root <path>] [--json]
```

Exit `0` when no blocking errors exist, `2` for readiness errors, `64` for invalid arguments. Neither command may initialize a workspace, run analysis, write config, install an LSP, launch a server, or make a model request.

## Contracts and folder placement

Create:

```text
src/Archy/Features/Diagnostics/
  RunDoctor/
  ReadConfiguredLanguages/
src/Archy/Features/CommandLine/Diagnostics/
  DoctorCli.cs
  DoctorListCli.cs
```

Use stable check IDs: `workspace.repository`, `workspace.state`, `workspace.database`, `configuration.effective`, `analysis.graph`, `verification.architecture`, `integrations.git_hooks`, `integrations.codex_mcp`, `embeddings.configuration`, `embeddings.credential`, `embeddings.cache`, and `languages.<profile-id>`.

Language inventory result:

```csharp
public enum LanguageProfileReadiness
{
    Ready, ConfiguredNoSources, CommandUnavailable,
    NoRepositoryMarker, InvalidConfiguration, Unknown
}

public sealed record ConfiguredLanguageProfileStatus(
    string Id, string LanguageId, IReadOnlyList<string> Extensions,
    IReadOnlyList<string> Markers, string Command, IReadOnlyList<string> Arguments,
    int MaxSymbolQueries, bool CommandAvailable, int MatchingSourceFileCount,
    IReadOnlyList<string> MatchedMarkers, LanguageProfileReadiness Readiness,
    string Detail, string? Remediation);
```

`doctor list` must show: profile ID, language ID, extensions, command, availability, source count, matched markers, readiness, and remediation. Reuse `LanguageServerProfiles`, `IExecutablePathProbe`, and bounded source inventory. Do not run LSP processes. A missing command is an error only when matching in-scope source exists and semantic coverage depends on it; otherwise warning/info.

## D — atomic tasks

- [ ] **D-001** Create doctor/language inventory contracts, `DoctorSeverity`, `DoctorCheck`, `DoctorReport`, `DoctorContext`, and `IDoctorCheck`.
- [ ] **D-002** Resolve `DoctorContext` without creating state: locate repository, load effective configuration, resolve potential workspace state location, read existing active revision, and retain prerequisite failures as values.
- [ ] **D-003** Implement repository/worktree, workspace-manifest, database-integrity, and effective-configuration checks using existing workspace/database abstractions.
- [ ] **D-004** Implement active-graph/analysis completeness check; report existing run/degradation facts but do not trigger analysis.
- [ ] **D-005** Implement `ReadConfiguredLanguagesHandler`: iterate every effective `LanguageServerProfileConfiguration`, probe command availability, count matching in-scope sources, resolve markers, and assign readiness/remediation deterministically.
- [ ] **D-006** Implement one doctor language check per profile (`languages.<profile-id>`); preserve profile ordering for data and sort rendered checks by ID.
- [ ] **D-007** Implement verification readiness, Git hooks, Codex/MCP executable, embedding config/key-availability/cache-statistics checks. Never expose key values or vectors.
- [ ] **D-008** Implement `RunDoctorHandler` with bounded, cancellation-aware checks and explicit abstentions rather than crashes.
- [ ] **D-009** Register handlers/services in `Program.cs`; use existing singleton/stateless lifetime conventions.
- [ ] **D-010** Implement `DoctorCli` human card + source-generated JSON output; include counts by severity and remediation.
- [ ] **D-011** Implement `DoctorListCli` compact table + source-generated JSON records; route `doctor`, `doctor list`, and help through `ArchyCli`.
- [ ] **D-012** Add capability endpoint/readiness exposure only after CLI contract stability; no doctor web screen is needed here.
- [ ] **D-013** Update README, user guide, configuration docs, and troubleshooting with doctor severity/recommendation and configured-language examples.

## D tests

- [ ] **D-014** Unit-test severity aggregation, deterministic order, and no-side-effect context resolution.
- [ ] **D-015** Unit-test language states: ready, configured/no sources, command unavailable with sources, no marker, and invalid profile.
- [ ] **D-016** Integration-test default C# plus a second fake language profile; assert both appear in `doctor list` to prevent language hardcoding.
- [ ] **D-017** Integration-test missing workspace db/graph and malformed config produce actionable checks without initializing/migrating state.
- [ ] **D-018** Integration-test configured embeddings with disabled consent/missing key/cache statistics; assert no credential/vector leakage.
- [ ] **D-019** Process-test both doctor commands’ JSON contracts, exit codes, stdout/stderr, and unchanged graph revision/worktree/database migration state.

---

# Epic H — Hybrid similarity and reuse intelligence

## User outcomes

Given a request, snippet, file, or existing symbol, Archy should return the best existing implementations and explain precisely why to reuse, extend, or avoid each one.

It must combine four evidence families, rather than privileging embeddings blindly:

1. semantic embeddings;
2. symbols/signatures/names;
3. dependency-neighborhood similarity;
4. file/module/placement context.

It must also surface duplicate/near-duplicate families and detect when new code recreates an older removed/renamed capability.

## Data model and contracts

Create `src/Archy/Features/Similarity/` with these slices:

```text
Similarity/
  RetrieveHybridCandidates/
  ExplainReuseDecision/
  BuildSimilarityClusters/
  DetectReintroducedCapability/
  ReadSimilarityHistory/
```

Core contracts:

```csharp
public enum SimilarityEvidenceKind
{
    Embedding, Symbol, Signature, DependencyNeighborhood,
    FileContext, ModuleContext, CloneSignal, HistoricalRevision
}

public sealed record HybridSimilarityWeights(
    double Embedding, double Symbol, double Signature,
    double DependencyNeighborhood, double FileContext, double ModuleContext);

public sealed record HybridSimilarityCandidate(
    string StableId, string DisplayName, string? FilePath,
    double Score, IReadOnlyList<SimilarityEvidence> Evidence,
    IReadOnlyList<string> ClusterIds);

public enum ReuseRecommendation { Reuse, Extend, DoNotReuse, InsufficientEvidence }

public sealed record ReuseExplanation(
    string CandidateStableId, ReuseRecommendation Recommendation,
    IReadOnlyList<ReuseFactor> SupportingFactors,
    IReadOnlyList<ReuseFactor> DifferentiatingFactors,
    IReadOnlyList<string> SuggestedNextChecks);
```

Default weights must be versioned/configurable but bounded and sum to `1.0`. Do not persist opaque model scores without their model ID, source revision, and evidence metadata.

## H — atomic tasks

### H.1 Hybrid retrieval

- [ ] **H-001** Create `RetrieveHybridCandidates` contracts, input validation, and a transport-neutral request that accepts intent, snippet, source file, source stable ID, optional intended module/path, and a limit `1..50`.
- [ ] **H-002** Extract the current logic from `FindSimilarMcpTool` into a shared similarity application service. MCP, CLI, proactive hooks, and web APIs must call this service; no duplicate cosine/consent/path-validation logic.
- [ ] **H-003** Preserve existing structural fallback behavior and its response compatibility before adding new evidence.
- [ ] **H-004** Implement `SymbolSimilarityScorer`: normalized display name, canonical key, fully qualified name, tokenized namespace/path, and exact name boosts. Keep tokenization language-neutral.
- [ ] **H-005** Implement `SignatureSimilarityScorer`: compare normalized signatures/return metadata/parameter metadata where available; abstain, do not infer, when absent.
- [ ] **H-006** Implement `DependencyNeighborhoodSimilarityScorer`: weighted Jaccard over direct incoming/outgoing neighbor IDs; report direction separately; cap neighborhood size deterministically.
- [ ] **H-007** Implement `FileContextSimilarityScorer`: same directory, path-segment overlap, source extension/language profile, and co-change evidence when present.
- [ ] **H-008** Implement `ModuleContextSimilarityScorer`: same configured module/layer, allowed dependency overlap, and placement-overlap score. Never claim a module where no layer/module matches.
- [ ] **H-009** Adapt existing embedding scoring so it only runs when compatible dimensions/model IDs are present. Mark missing embedding evidence as unavailable, never negative.
- [ ] **H-010** Implement a deterministic score combiner that renormalizes over *available* evidence weights; absent evidence must not penalize a candidate.
- [ ] **H-011** Add score confidence bands (`high`, `medium`, `low`, `insufficient`) based on evidence diversity and score—not a model hallucination.
- [ ] **H-012** Add a deterministic tie-breaker: score desc, confidence desc, source revision recency desc, stable ID ordinal.
- [ ] **H-013** Return per-component raw/normalized contributions in structured output. Human text may summarize; raw numbers must remain accessible to adapters.
- [ ] **H-014** Add `similarity` configuration section only after parsing/validation/migration design is complete. Defaults must preserve current behavior for existing configs.

### H.2 “Why not reuse?”

- [ ] **H-015** Create `ExplainReuseDecision` feature contracts. Input: proposed request/snippet plus one candidate; output: reuse recommendation, supporting factors, differentiating factors, risks, and required checks.
- [ ] **H-016** Implement structural differentiators: differing public signatures, dependency/layer direction, file/module ownership, visibility, and graph role.
- [ ] **H-017** Implement behavioral differentiators only from bounded persisted summaries/decisions and explicitly label them advisory.
- [ ] **H-018** Add “reuse” threshold rules: a candidate is never automatically recommended merely because embedding score is high; require at least one structural corroborator or report `InsufficientEvidence`.
- [ ] **H-019** Add “extend” recommendation when candidate shares intent but has incompatible public surface/module constraints.
- [ ] **H-020** Add “do not reuse” recommendation only with concrete conflicts: forbidden dependency, incompatible layer, incompatible public contract, or explicit decision evidence.
- [ ] **H-021** Produce exact next checks, e.g. `get_dependents`, `check_violation`, `suggest_placement`, and focused test commands only when project evidence supports them.
- [ ] **H-022** Add `why_not_reuse` as a dedicated MCP tool instead of overloading `find_similar`; include candidate stable ID and source input schema.

### H.3 Similarity clusters

- [ ] **H-023** Define cluster identity/version contracts under `BuildSimilarityClusters`.
- [ ] **H-024** Add SQLite migration for cluster revisions, cluster identities, members, evidence summary, algorithm/version, and graph revision. Reuse existing cluster table patterns if semantically appropriate; do not corrupt placement clusters.
- [ ] **H-025** Build candidate edges only above conservative hybrid-score and evidence-diversity thresholds; do not make an all-pairs O(n²) pass for unbounded repositories.
- [ ] **H-026** Use existing bounded candidate retrieval/partitioning to limit comparisons. Document corpus and time budgets.
- [ ] **H-027** Implement deterministic connected components or a documented community algorithm with a fixed version/seed. Persist algorithm metadata.
- [ ] **H-028** Generate cluster labels from stable symbol/path evidence, not a model call.
- [ ] **H-029** Exclude generated source unless configured; exclude one-member clusters; preserve source-location references.
- [ ] **H-030** Add `similarity clusters` CLI query and `get_similarity_cluster` MCP tool.
- [ ] **H-031** Add lifecycle reconciliation: clusters disappear/stale when graph revision changes; historical revisions remain queryable.

### H.4 Cross-revision reintroduction detection

- [ ] **H-032** Define `HistoricalCapabilityFingerprint`: normalized signature, declaration kind, path/module context, bounded structural tokens, and content hash where available.
- [ ] **H-033** Implement a history reader over graph node/symbol version tables that finds prior nodes no longer active in the current revision.
- [ ] **H-034** Implement rename/move matching: same stable semantic identity first, then signature/path/name evidence; never label a rename as a duplicate without confidence/evidence.
- [ ] **H-035** Compare a new/changed declaration to removed historical fingerprints using hybrid retrieval restricted to prior revisions.
- [ ] **H-036** Add `reintroduced_capability` evidence only when score threshold and at least two independent evidence families are present.
- [ ] **H-037** Include historical revision, previous path, removal revision, and any linked decision/session evidence in results.
- [ ] **H-038** Add `find_reintroduced` CLI/MCP tool; it must abstain cleanly when history was compacted/unavailable.

## H tests and acceptance

- [ ] **H-039** Unit-test each scorer, score renormalization, tie-breaks, unavailable embeddings, and thresholds.
- [ ] **H-040** Unit-test reuse explanations for reuse/extend/do-not-reuse/insufficient cases.
- [ ] **H-041** Integration-test clusters against a synthetic graph with controlled structural and embedding signals.
- [ ] **H-042** Integration-test historical reintroduction after an active node is removed and a similar node is added later.
- [ ] **H-043** Regression-test that no raw vector/source/key is emitted by CLI, MCP, web DTOs, logs, or clusters.
- [ ] **H-044** Benchmark candidate retrieval with a representative 2k+ node corpus and enforce bounded comparison count.

---

# Epic C — change planning, impact analysis, safe refactoring, explanation

## User outcomes

`plan_change` tells an agent where a requested change belongs, what existing code to reuse, what files/modules are likely involved, rules/decisions that apply, impact/risk, and validation steps.

`impact_analysis` gives direct/transitive dependents, public API blast radius, layer implications, configuration/message-contract impact, and explicit truncation.

`safe_refactor` produces a sequenced, checkpointed migration plan. It does not edit code.

`explain_architecture` answers “why is this here?” from facts, decisions, summaries, and history with source provenance.

Create vertical slices:

```text
Features/Planning/
  PlanChange/
  AnalyzeImpact/
  PlanSafeRefactor/
  ExplainArchitecture/
```

## C — atomic tasks

### C.1 Shared plan model

- [ ] **C-001** Create immutable shared planning contracts: `PlanningEvidence`, `PlanningRisk`, `ValidationCheckpoint`, `PlanningAbstention`, provenance/revision metadata.
- [ ] **C-002** Require every plan result to carry active graph revision and analysis-completeness state.
- [ ] **C-003** Define stable risk IDs: `public_api`, `high_blast_radius`, `forbidden_layer_direction`, `cycle_risk`, `configuration_contract`, `message_contract`, `incomplete_analysis`, `missing_history`, `similarity_ambiguous`.
- [ ] **C-004** Define a bounded plan budget: max candidates 20, max impact nodes 500, max decisions/summaries 20 each; expose truncation.

### C.2 `impact_analysis`

- [ ] **C-005** Create input contract: target stable ID, direction (`dependents`, `dependencies`, `both`), depth `1..8`, max nodes `1..500`, optional edge kinds.
- [ ] **C-006** Reuse/extend graph traversal readers to perform deterministic breadth-first traversal with per-hop path evidence.
- [ ] **C-007** Return direct and transitive results separately; never flatten depth into one unexplained list.
- [ ] **C-008** Identify public API blast radius: active public symbols reached, external-facing contracts, and interface fingerprints.
- [ ] **C-009** Identify layer implications: current target layer, traversed layers, illegal direction evidence, and potential cycle endpoints.
- [ ] **C-010** Identify configuration effects through existing configuration-read/definition facts.
- [ ] **C-011** Identify message-contract effects through existing messaging/message-contract facts.
- [ ] **C-012** Identify DI/service registration effects through existing dependency registration/consumption facts.
- [ ] **C-013** Build risk score only as prioritization; retain individual evidence so it is explainable.
- [ ] **C-014** Return explicit abstention for unknown target/no active graph/degraded semantic coverage.

### C.3 `plan_change`

- [ ] **C-015** Create input: natural-language request, optional intended files, optional target symbol, optional intended layer/module, optional code snippet.
- [ ] **C-016** Normalize/validate all paths against repository root; reject traversal and cap request/snippet sizes.
- [ ] **C-017** Invoke hybrid similarity to identify reuse candidates; attach `why_not_reuse` output for top three only.
- [ ] **C-018** Invoke existing placement advisor for likely owning feature folders/modules; do not duplicate placement scoring.
- [ ] **C-019** Resolve candidate impact with bounded `impact_analysis` calls.
- [ ] **C-020** Resolve applicable layer rules, architecture exceptions, decisions, and active session context.
- [ ] **C-021** Generate likely affected files from persisted graph paths only; distinguish `existing files` from `suggested new files`.
- [ ] **C-022** Generate risks from evidence; no generic boilerplate risks.
- [ ] **C-023** Generate validation steps from repository facts: analysis/verify always; focused project/test commands only when project mapping supports them.
- [ ] **C-024** Produce a recommendation: reuse/extend/new capability, with confidence and abstentions.

### C.4 `safe_refactor`

- [ ] **C-025** Create input: target symbol/module, refactor intent (`move`, `split`, `merge`, `rename`, `extract`, `replace`), optional destination/path.
- [ ] **C-026** Refuse ambiguous or unsupported intents rather than inventing code transformations.
- [ ] **C-027** Derive prerequisite graph facts: dependents, dependencies, public surface, layer membership, registrations, config/messages, similar clusters.
- [ ] **C-028** Build sequenced phases: baseline → isolate → migrate callers → preserve compatibility → remove old path → verify.
- [ ] **C-029** Include concrete safety checkpoints after each phase: expected graph/rule state and exact Archy check.
- [ ] **C-030** For public APIs, include compatibility strategy (adapter/deprecation/parallel contract) only when graph facts establish a public surface.
- [ ] **C-031** For moves/renames, include history fingerprint expectations so reintroduction detection does not create false positives.
- [ ] **C-032** Add rollback guidance that is procedural, not an unsafe automatic rollback command.

### C.5 `explain_architecture`

- [ ] **C-033** Create input: stable ID/path/module/free-text lookup and optional history depth.
- [ ] **C-034** Resolve graph facts first: declaration, file/module/layer, direct edges, source evidence, public surface.
- [ ] **C-035** Add decisions with author/date/status and scope; distinguish expired/superseded decisions.
- [ ] **C-036** Add summaries only as advisory context with batch/revision metadata.
- [ ] **C-037** Add historical revisions: introduction/move/rename/removal evidence, bounded by requested depth.
- [ ] **C-038** Produce an answer segmented into `Facts`, `Decisions`, `Advisory context`, `Unknowns`, and `Suggested next questions`.

### C.6 Adapters

- [ ] **C-039** Add CLI commands under `Features/CommandLine/Planning/`: `change plan`, `impact analyze`, `refactor plan`, `architecture explain`.
- [ ] **C-040** Add MCP tools: `plan_change`, `impact_analysis`, `safe_refactor`, `explain_architecture` with strict JSON schemas and concise descriptions.
- [ ] **C-041** Register tools in `McpCli`; update MCP count/process tests and README/tool reference.

## C tests and acceptance

- [ ] **C-042** Unit-test traversal direction/depth/caps/path construction and risk classification.
- [ ] **C-043** Integration-test public API blast radius, layer implication, configuration read, message contract, and DI impact.
- [ ] **C-044** Integration-test `plan_change` uses hybrid candidates, placement, decisions, and abstentions without a model.
- [ ] **C-045** Integration-test `safe_refactor` phase/checkpoint generation for public and internal targets.
- [ ] **C-046** Integration-test `explain_architecture` separates facts from advisory summaries and returns historical provenance.
- [ ] **C-047** Process-test CLI JSON schemas/exit codes and MCP stdio responses.

---

# Epic P — proactive Codex integration

## User outcome

Archy helps before and after agent edits without pretending it can stop arbitrary writes. It supplies relevant context early, detects probable reuse/duplication after a change, and records a graph-based change summary for the session/PR workflow.

Use existing Codex hook architecture. Do not introduce a hidden background agent or direct access to a user’s remote PR provider.

Create vertical slices:

```text
Features/Integrations/Codex/
  PreflightChange/
  AdvisePostEditReuse/
  ComposeSessionArchitectureContext/
  ComposeChangeSummary/
```

## P — atomic tasks

### P.1 Preflight MCP tool

- [ ] **P-001** Add `preflight_change` MCP tool input: natural-language change description, intended repository-relative paths, optional target stable IDs, optional code snippet.
- [ ] **P-002** Validate intended paths and cap path count (e.g. 50) and description/snippet length.
- [ ] **P-003** Invoke `plan_change`, `impact_analysis`, relevant `check_violation`, `get_module_rules`, and `suggest_placement` queries in a bounded orchestration service.
- [ ] **P-004** Return sections: target placement, existing reuse candidates, hard rules, impact, risks, decisions, verification checklist, abstentions.
- [ ] **P-005** Mark result `advisory=true`; never claim preflight blocks a future write.
- [ ] **P-006** Add tests for path traversal, unavailable graph, multiple intended files, and rule/placement evidence.

### P.2 Post-edit reuse guidance

- [ ] **P-007** Extend existing post-tool changed-path resolution; preserve its bounded path/security guarantees.
- [ ] **P-008** After `Edit`/`Write`, map changed paths to active/incremental graph nodes. If graph is stale, say so explicitly rather than analyzing unsaved text as a graph fact.
- [ ] **P-009** For changed declarations/files, run bounded hybrid similarity against existing corpus excluding the exact current stable ID.
- [ ] **P-010** Detect high-confidence probable duplication only when semantic/structural evidence meets thresholds and at least one existing candidate is in a different feature/module.
- [ ] **P-011** Attach `why_not_reuse` explanation for the strongest candidate, with a concise remediation: reuse, extract shared component, or explicitly justify difference.
- [ ] **P-012** Publish a structured hook event; do not dump full source code into hook output.
- [ ] **P-013** Ensure a model/embedding failure degrades to structural evidence and never makes the hook fail closed unless an existing deterministic verification finding already does.
- [ ] **P-014** Add unit/integration tests for identical change, changed-path exclusion, stale graph, consent disabled, cached embeddings, and generated query evidence.

### P.3 Session-aware context

- [ ] **P-015** Define a bounded `SessionArchitectureContext` record: active graph revision, focused paths/nodes, layer rules, recent decisions, nearest similar clusters, session change summary, and abstentions.
- [ ] **P-016** On `SessionStart`, compute context from repository root and optional prompt/path hints. Preserve current hook behavior if no hints are available.
- [ ] **P-017** Mine prompt/path hints locally; do not send a session prompt to a model merely to classify it.
- [ ] **P-018** Rank context items by path/module proximity, decision scope, recent session touches, and active planning target.
- [ ] **P-019** Cap emitted context by item count and characters; include links/stable IDs for drill-down instead of large summaries/source.
- [ ] **P-020** When a `preflight_change` result exists in the session, attach its plan ID/revision and invalidate it when graph revision changes.
- [ ] **P-021** Extend session persistence only with a migration and first-class event types; do not serialize arbitrary MCP payloads wholesale.
- [ ] **P-022** Add tests for relevance ranking, caps, redaction, revision invalidation, and no-session fallback.

### P.4 Graph-delta change/PR summary

- [ ] **P-023** Define `ArchitectureChangeSummary`: base/current graph revision, added/removed/changed nodes/edges/public surfaces/config/message contracts, deterministic findings, advisory risks, decisions touched, and verification state.
- [ ] **P-024** Determine base revision from session start or explicit user-supplied revision. Abstain if no trustworthy base exists; do not assume Git HEAD alone equals graph revision.
- [ ] **P-025** Implement graph delta reader over node/edge/symbol version tables with bounded pagination and deterministic ordering.
- [ ] **P-026** Classify deltas into architecture language: boundary movement, public API, dependency direction, configuration, messaging, persistence, deletion, and unknown.
- [ ] **P-027** Add `compose_change_summary` MCP tool and CLI command. Return Markdown-ready text plus structured data; never post to GitHub automatically.
- [ ] **P-028** At session stop, persist summary-batch linkage and emit a concise hook event. Do not write a Git commit message or PR description unless an explicit downstream integration invokes the tool.
- [ ] **P-029** Add tests for additions/removals/moves, public surface changes, stale base revision, paged deltas, and clean no-change summary.

### P.5 Documentation and safeguards

- [ ] **P-030** Update Codex integration docs with the exact distinction: preflight/post-edit guidance is advisory; hooks cannot undo completed writes.
- [ ] **P-031** Add privacy documentation for query embeddings, session context, and persisted change summaries.
- [ ] **P-032** Add feature flags/config defaults for proactive advisory behavior; defaults must remain non-invasive and source-sharing-disabled.

## P tests and acceptance

- [ ] **P-033** MCP stdio process test all new tools; assert stdout stays JSON-RPC only.
- [ ] **P-034** Integration-test hook event publication and session persistence with redacted/bounded payloads.
- [ ] **P-035** Test deterministic enforcement remains unchanged when all proactive features are disabled.
- [ ] **P-036** Test consent-disabled mode still provides structural guidance and never calls the embedding provider.

---

# Delivery order and gates

## Recommended commit sequence

1. `feat: add hybrid similarity retrieval contracts and structural scorers`
2. `feat: add reuse explanations and similarity clusters`
3. `feat: detect reintroduced capabilities across graph revisions`
4. `feat: add impact analysis and change planning tools`
5. `feat: add safe refactor and architecture explanation tools`
6. `feat: add proactive Codex preflight and post-edit reuse guidance`
7. `feat: add session context and graph delta change summaries`
8. `feat: add non-mutating doctor diagnostics and configured-language inventory`
9. `docs: document advanced architecture intelligence workflows`

## Required gates after each cohesive batch

```sh
dotnet test tests/Archy.UnitTests/Archy.UnitTests.csproj --no-restore
dotnet test tests/Archy.IntegrationTests/Archy.IntegrationTests.csproj --no-restore
dotnet build src/Archy/Archy.csproj --configuration Release --no-restore
npm ci && npm run build --prefix ui/archy-web
zsh scripts/verify-architecture-contracts.sh
archy verify --path .
```

## Final acceptance checklist

- [ ] Hybrid scores are explainable, bounded, and gracefully degrade without embeddings.
- [ ] Reuse recommendations are never based on embeddings alone.
- [ ] Clusters and history are revisioned, queryable, and bounded.
- [ ] Change/refactor/explanation tools use graph facts and clearly label advisory evidence/unknowns.
- [ ] Proactive hooks do not claim to block writes and do not leak source/key/vector data.
- [ ] All new MCP tools have schemas, stdio tests, descriptions, and documentation.
- [ ] `archy doctor list` reports every configured language profile with command/source/marker/readiness evidence and no language-specific hardcoding.
- [ ] Full .NET/UI/release/architecture gates pass on Archy itself.
