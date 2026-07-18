# Embedding indexing: implementation handoff

## Objective

Make semantic similarity user-operable in Archy. A user must be able to index eligible C# method chunks into Archy's local SQLite embedding cache, then use `find_similar` with a source stable ID and model ID to receive cached cosine-similarity evidence.

This feature must preserve Archy's privacy model: repository source may leave the machine only after explicit repository consent and only through the configured model provider.

## Current repository state

The repository already contains most of the lower-level primitives:

- `src/Archy/Features/Duplicates/EmbeddingCache/EmbeddingCacheResolver.cs`
  - Reuses cache entries by `(methodStableId, modelId, contentHash)`.
  - Generates only missing vectors through an `IModelProvider`.
  - Persists generated vectors with graph-revision provenance.
- `src/Archy/Features/Duplicates/EmbeddingCache/EmbeddingCacheRepository.cs`
  - Supports exact read/store and an added `ListByModelAsync` method.
- `src/Archy/Features/Duplicates/SelectEmbeddingChunks/CSharpEmbeddingChunkSelector.cs`
  - Produces method-only chunks. It intentionally excludes enclosing types, imports, and unrelated siblings.
- `src/Archy/Features/Memory/AuthorizeAiSourceSharing/RepositoryAiConsentPolicy.cs`
  - Requires `AiSourceSharingMode.SummariesAndEmbeddings` for embedding work.
- `src/Archy/Features/Memory/GovernModelRequests/ModelRequestGovernor.cs`
  - In-memory request/token/cost/concurrency admission control.
- `src/Archy/Features/Memory/ModelProviders/OpenAi/OpenAiResponsesModelProvider.cs`
  - OpenAI provider implementing embeddings.
- `src/Archy/Features/Graph/ReadGraphRevision/GraphRevisionSnapshotReader.cs`
  - Reads active nodes, edges, symbols, and interfaces.
- `src/Archy/Features/Queries/FindSimilar/`
  - New deterministic similarity engine.
- `src/Archy/Features/Integrations/Mcp/RunMcpServer/FindSimilarMcpTool.cs`
  - New MCP tool. It already reads cached compatible vectors for a supplied `sourceStableId` + `embeddingModel` and adds cosine evidence.

An incomplete new core slice also exists:

- `src/Archy/Features/Duplicates/IndexEmbeddings/EmbeddingIndexer.cs`
  - Enforces consent and configured provider/model before calling `IEmbeddingCacheResolver`.
  - It currently needs governor integration, CLI orchestration, tests, and dependency registration.
- `src/Archy/Features/CommandLine/Embeddings/EmbeddingsCli.cs`
  - This is only a placeholder/help shell. Do **not** present `index` or `status` as supported until their handlers exist.

## Required user contract

### Configuration

Embedding generation is disabled unless repository `archy.toml` contains:

```toml
[model]
provider = "openai"
embedding_model = "text-embedding-3-large"
max_requests_per_run = 30
max_tokens_per_run = 200000

[memory]
source_sharing = "summaries_and_embeddings"
```

Relevant types are in `Features/Configuration/LoadEffectiveConfiguration/ArchyConfiguration.cs`:

- `ModelConfiguration.Provider`
- `ModelConfiguration.EmbeddingModel`
- `MemorySelectionConfiguration.SourceSharing`
- `AiSourceSharingMode.SummariesAndEmbeddings`

Credentials must remain outside TOML. The OpenAI provider gets them through the existing `EnvironmentOpenAiApiKeyProvider` (`OPENAI_API_KEY`).

### CLI

Implement these commands:

```sh
archy embeddings index [--path <path>] [--model <model-id>] [--max-chunks <1-2048>] [--dry-run] [--json]
archy embeddings status [--path <path>] [--model <model-id>] [--json]
```

Rules:

- `--model` overrides the configured embedding model only if the configured provider supports embeddings.
- `index` must first ensure an active graph. It may invoke normal analysis or return a clear instruction to run `archy analyze`; choose one policy and test it. Prefer returning a clear non-zero result rather than silently indexing stale/no graph data.
- `--dry-run` must perform no provider call and no cache writes. It reports exactly how many chunks are eligible, already cached, and would be sent.
- `--max-chunks` must be bounded from 1 to 2048, defaulting to a conservative value no greater than configuration `MaxRequestsPerRun` / provider batching policy.
- `status` is read-only. It reports configured consent/provider/model state, active graph revision, cached vector count for the selected model, dimensions distribution, and latest cache timestamp. Do not expose vector contents.
- All machine-readable output must be valid JSON when `--json` is selected.

### MCP

Keep `find_similar` read-only. It must never create embeddings as a side effect.

Its input schema should document:

```json
{
  "query": "create an architecture session",
  "sourceStableId": "method:...",
  "embeddingModel": "text-embedding-3-large",
  "limit": 10
}
```

Its response must distinguish:

- `embeddingEvidence: "cached"`: compatible cached source/candidate vectors were used.
- `embeddingEvidence: "unavailable"`: no compatible cache evidence. Include a concise reason, such as missing source vector, model mismatch, or no active graph.

Never claim that unavailable evidence means no semantic match exists.

## Architecture to implement

Create a vertical slice under:

```text
src/Archy/Features/Duplicates/IndexEmbeddings/
  IndexEmbeddingsCommand.cs
  IndexEmbeddingsHandler.cs
  EmbeddingIndexRequest.cs
  EmbeddingIndexer.cs
  EmbeddingIndexResult.cs
  EmbeddingIndexPlan.cs
  EmbeddingIndexStatusQuery.cs
  EmbeddingIndexStatusHandler.cs
  EmbeddingIndexStatus.cs
  IEmbeddingIndexSourceReader.cs
  EmbeddingIndexSourceReader.cs
```

Keep CLI formatting/parsing under:

```text
src/Archy/Features/CommandLine/Embeddings/
  EmbeddingsCli.cs
  IndexEmbeddingsCli.cs
  EmbeddingStatusCli.cs
```

Do not put database SQL, provider behavior, and terminal formatting in one file.

### Source extraction

`EmbeddingIndexSourceReader` should:

1. Read the active `GraphRevisionSnapshot` using `IGraphRevisionSnapshotReader`.
2. Select graph nodes that are actual C# methods with non-null file path and valid line range.
3. Resolve file paths safely beneath repository root. Reject traversal/out-of-root paths.
4. Read bounded source files (use the existing source-size limits; add a defensive maximum if no shared limit exists).
5. Construct `EmbeddingChunkSource` values.
6. Use `IEmbeddingChunkSelector` to construct method-only chunks.

Important-node eligibility should be deliberate. The existing selector takes `ImportantNodeEligibility` and currently only accepts `PublicApi` targets. Either:

- use `IImportantNodeEligibilityPolicy` with a correctly constructed request; or
- introduce a dedicated embedding eligibility policy that explicitly documents why selected methods are eligible.

Do not fake every method as a `PublicApi` target simply to satisfy the selector. If broad indexing is desired, change the selector/eligibility contract explicitly and add tests.

### Consent and governance sequence

The exact sequence for `index` is:

1. Locate/init workspace via existing workspace commands/handlers.
2. Load effective configuration through `LoadEffectiveConfigurationQuery`.
3. Evaluate `IRepositoryAiConsentPolicy` with `AiSourceSharingOperation.Embedding`.
4. Validate provider selection and embedding model.
5. Read active graph and form a bounded chunk plan.
6. Calculate/cache-hit plan without outbound calls.
7. For a real run, reserve model capacity through `IModelRequestGovernor.TryReserve` using a conservative `ModelRequestEstimate`.
8. Call `IEmbeddingCacheResolver.ResolveAsync` with the selected chunks and configured `IModelProvider`.
9. Always call `IModelRequestGovernor.Complete` in `finally` when a lease was acquired.
10. If provider failure is rate-limited, call `ApplyRateLimitCooldown`; return a safe, actionable failure.
11. Return cache hit/generated/failed counts and model usage. Never output source chunks.

Provider resolution must be explicit:

- `provider = "openai"` => resolve `OpenAiResponsesModelProvider` from DI.
- `provider = "disabled"` => deterministic non-zero failure: no provider is enabled.
- Unknown provider => validation failure.

Avoid constructing an `HttpClient`, API-key provider, or OpenAI provider inside command handlers.

## Required DI and CLI wiring

In `src/Archy/Program.cs`:

- Register `EmbeddingIndexer` and all new interfaces/services.
- Register Mediator handlers in the same style as existing feature handlers.
- Reuse registered `IEmbeddingCacheResolver`, `IEmbeddingCacheRepository`, `IEmbeddingChunkSelector`, `IRepositoryAiConsentPolicy`, `IModelRequestGovernor`, `OpenAiResponsesModelProvider`, and graph readers.

In `src/Archy/Features/CommandLine/ArchyCli.cs`:

- Route `embeddings` to `EmbeddingsCli.RunAsync(args[1..], mediator, serviceProvider, cancellationToken)`.
- Only advertise commands that are actually implemented.

## Tests required

### Unit tests

Add under `tests/Archy.UnitTests/Features/Duplicates/IndexEmbeddings/`:

1. Consent disabled rejects before provider/resolver use.
2. Missing embedding model rejects.
3. Disabled/mismatched provider rejects.
4. Request governor denial prevents provider use.
5. Cache-hit-only run returns zero generated vectors and does not invoke provider.
6. Generated run completes governor lease even on provider failure.
7. Chunk bound is enforced deterministically.

### Integration tests

Add under `tests/Archy.IntegrationTests/Features/Duplicates/IndexEmbeddings/`:

1. Create a temporary repository, initialize state, commit method graph nodes, write source files, run index with `DeterministicModelProvider`, then verify cache entries exist with expected model/revision/content hashes.
2. Re-run unchanged content and verify cache reuse (`GeneratedCount == 0`).
3. Change method content, commit new graph revision/content hash, re-run and verify a new cache row is used.
4. Verify `find_similar` returns `embeddingEvidence: "cached"` after indexing; do not seed cache manually for this test.
5. Verify status returns counts but no vector JSON.

### Executable CLI tests

Extend the existing `ArchyProcess`/`LocalProcess` test infrastructure:

1. `archy embeddings index --dry-run --json` returns an accurate plan and does not create cache rows.
2. With consent disabled, executable exits non-zero with actionable JSON/text and makes no provider request.
3. With deterministic provider test wiring, executable index then status succeeds.

Real OpenAI network calls must not run in unit/integration CI. Use `DeterministicModelProvider` through a test-only provider selection/seam.

## Documentation updates

Update `README.md` with:

- required `archy.toml` configuration,
- explicit source-sharing warning,
- `OPENAI_API_KEY` setup without putting secrets in TOML,
- index/dry-run/status examples,
- `find_similar` usage after indexing,
- statement that embeddings are stored in machine-local Archy state, not repository files.

Update `docs/user-guide.md` to replace the current future-tense embedding language with actual commands.

Update `docs/configuration.md` with the precise model/memory settings.

Update `FindSimilarMcpTool.Description` and `InputSchemaJson` to state cached semantic evidence requirements. Ensure `tools/list` exposes the updated description.

## Acceptance criteria

Do not mark this complete until all are true:

- `archy embeddings index --dry-run` works without API credentials or outbound calls.
- Real index requires `summaries_and_embeddings` repository consent.
- Real index reuses cached content hashes and only calls the provider for missing chunks.
- Provider admission obeys request/token/cost/concurrency governor limits.
- `archy embeddings status` is read-only and reports useful cache state.
- `find_similar` receives real cached embedding evidence after CLI indexing.
- All new unit, integration, and executable tests pass.
- Existing `zsh scripts/test.sh`, web tests/build, architecture contracts, and `archy verify` pass.
- README and MCP tool descriptions describe only shipped behavior.
