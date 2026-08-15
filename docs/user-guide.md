# Archy user guide

## Install on macOS

Download the release archive that matches `uname -m` (`arm64` for Apple Silicon, `x64` for Intel), download its adjacent `.sha256` file, and verify it before extracting it:

```sh
shasum -a 256 -c archy-<version>-osx-<architecture>.tar.gz.sha256
tar -xzf archy-<version>-osx-<architecture>.tar.gz
cd archy-<version>-osx-<architecture>
./install.sh
```

The installer verifies every packaged file before writing to `${ARCHY_INSTALL_PREFIX:-$HOME/.local}`. Add `$HOME/.local/bin` to `PATH`, then check `archy --version`. Uninstall with the `uninstall.sh` that shipped in the same verified release directory. Archy’s executable is Native AOT and does not need a globally installed .NET runtime.

The optional bundled sidecars have separate runtime requirements: `jscpd` needs Node and the Louvain worker needs Python. A missing runtime makes that advisory capability unavailable; it does not silently change deterministic verification into a pass.

## Quickstart

Run these commands in exactly one Git repository:

```sh
archy workspace init --path .
archy analyze --path .
archy verify --path .
archy web serve --path .
```

The web host binds to loopback only. It serves the graph, bounded traversal, provenance, decisions, health snapshots, deterministic query grammar, and durable session replay. It does not expose a remote server by default.

Use `archy mcp stdio` for a local MCP client. The Codex plugin in `plugins/archy` uses that transport. Start a session, preflight a change, record decisions, and end the session through the provided MCP tools. A post-write hook can stop further agent work after a deterministic violation, but no hook can claim to undo a write that already happened.

Run `archy doctor --path . --json` for a read-only readiness report before initialization or when an integration is degraded. `archy doctor list --path . --json` inventories all effective language profiles. Both commands avoid analysis, migrations, LSP launches, and model calls.

## Find similar existing code

Use the `find_similar` MCP tool before introducing a new service, handler, or abstraction. It ranks persisted graph candidates with separately reported symbol, structural-signature, and dependency-neighborhood evidence. Pass an existing `sourceStableId` to compare dependency neighborhoods. Passing both `sourceStableId` and `embeddingModel` additionally enables cached cosine-similarity evidence only when compatible vectors already exist for that exact model; the read-only tool never sends source code or generates a new embedding. Missing graph or vector evidence is reported as an abstention or unavailable evidence, never as a negative claim that no similar code exists.

## Plan without editing

The planning adapters read existing graph state and never edit the worktree:

```sh
archy architecture explain --lookup <stable-id-or-path> --json
archy change plan --description "add a session capability" --file src/Sessions/CreateSession.cs --json
archy impact analyze --target <stable-id> --json
archy refactor plan --target <stable-id> --intent move --destination src/NewArea/Target.cs --json
```

Their MCP counterparts are advisory as well. Run `archy analyze` when you need fresh graph facts, then use `archy verify --path .` for deterministic delivery enforcement.

To inspect or materialize conservative reuse families, use `archy similarity clusters --json` or `archy similarity clusters --build --json`. Building writes only an immutable local workspace-state revision, never the worktree. Use `archy similarity reintroduced --stable-id <id> --json` to check bounded removed-capability history.

## Index embeddings

After `archy analyze` has created an active graph, repositories that explicitly set `[memory] source_sharing = "summaries_and_embeddings"` and configure an OpenAI embedding model can index eligible public C# methods:

```sh
archy embeddings index --path . --dry-run
archy embeddings index --path .
archy embeddings status --path . --json
```

The dry run reports eligible, cached, and would-send chunks without an API call or cache write. Real indexing uses `OPENAI_API_KEY` from the environment and stores vectors only in machine-local Archy state. Use `--model` to select a configured-provider-compatible model and `--max-chunks` to bound one run.

## What Archy guarantees

Strict verification is limited to configured hard edge kinds with confidence `1.0`. It produces a non-zero verification outcome for introduced, deterministic violations. Baselines and exceptions are explicit repository policy artifacts and never permit an expired exception.

Duplicate detection, placement advice, model summaries, health scoring, and natural-language query assistance are advisory. They always retain their evidence and confidence, and unsupported deterministic queries request clarification rather than inventing an answer.

## Model, privacy, and recovery

The default model provider is OpenAI Responses API, enabled only when the user supplies `OPENAI_API_KEY`. Archy sends only source material that its consent and redaction policy authorizes for a requested summary. The local capability view reports model readiness without returning the key.

Workspace state, including graph revisions, summaries, decisions, and sessions, lives outside the repository. Back up before an upgrade with `archy db backup`; use `archy db check` after recovery and `archy db restore` only with an explicit backup path. A newer unsupported database schema fails closed rather than being silently rewritten.

## Limitations

C# has built-in syntax and framework analysis. JavaScript, JSX, TypeScript, and TSX sources are inventoried directly and have built-in standard-LSP profiles backed by `typescript-language-server`; install it together with `typescript` to enable semantic symbols, references, and outgoing calls. Other languages remain configuration-driven. A missing or unavailable LSP, model outage, or sidecar outage is surfaced as degraded coverage—not inferred as healthy architecture.
