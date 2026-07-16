# Archy

**Architecture memory for C# repositories.**

Archy analyzes one Git repository at a time, persists a versioned architecture graph outside the worktree, and gives developers and Codex a practical way to answer questions such as:

- What depends on this type, file, or boundary?
- Will this change break an architectural rule?
- Why does this dependency exist, and how confident is Archy about it?
- Which summaries, decisions, duplicate findings, and health signals apply to this part of the codebase?

It is a .NET 10 Native AOT application. The core runs locally on macOS today, needs no globally installed .NET runtime when installed from a release archive, and is designed around a C#-first configuration that can be extended with standard-LSP language profiles.

> Archy is deliberately honest about certainty. Deterministic, configured, confidence-1.0 architecture facts can enforce rules. Summaries, duplicate detection, placement suggestions, and health scoring are evidence-backed advisories—not pretend guarantees.

## What you can do today

- Build a revisioned C# architecture graph with source locations, evidence, confidence, provenance, and dependency traversal.
- Configure dependency layers and verify introduced deterministic violations locally or in CI; export findings as SARIF.
- Inspect the graph in the local React/Tailwind UI, including node/edge evidence, blast radius, session replay, health components, summary freshness, duplicate evidence, and placement clusters.
- Ask a small deterministic query grammar: `what uses …?`, `what does … use?`, and `what breaks if I delete …?`.
- Connect Codex through local MCP tools for preflight checks, rules, dependents, duplicate/placement advice, durable decisions, and session lifecycle events.
- Use Codex hooks to add startup context and stop a subsequent agent turn after a post-write deterministic violation. Hooks cannot undo a write that already happened.
- Keep all local graph/session/summary state outside your repository, with database check, backup, restore, vacuum, and diagnostics commands.
- Enable OpenAI Responses-backed summary work with a user-provided `OPENAI_API_KEY`; the key is never stored in `archy.toml` or returned by the UI/API.

## Quick start

### From a release archive (macOS)

Choose the matching archive for `uname -m` (`arm64` on Apple Silicon; `x64` on Intel), verify it, extract it, and install:

```sh
shasum -a 256 -c archy-<version>-osx-<architecture>.tar.gz.sha256
tar -xzf archy-<version>-osx-<architecture>.tar.gz
cd archy-<version>-osx-<architecture>
./install.sh
```

The installer verifies every bundled file before writing under `${ARCHY_INSTALL_PREFIX:-$HOME/.local}`. Add `$HOME/.local/bin` to your `PATH`, then confirm installation:

```sh
archy --version
```

### From this checkout

Use the project command while developing Archy itself:

```sh
dotnet run --project src/Archy -- --version
```

### Analyze a repository

From the target repository—not necessarily this one—run:

```sh
archy workspace init --path .
archy analyze --path .
archy verify --path .
```

`workspace init` discovers the one owning Git repository and creates Archy state outside the worktree. `analyze` builds or updates a graph revision. `verify` evaluates configured deterministic rules and returns a non-zero outcome for introduced violations.

Open the local UI with:

```sh
archy web serve --path .
```

Then visit `http://127.0.0.1:8788`. The web host is loopback-only and applies bounded request parsing plus browser security headers.

## Configure architecture rules

Archy loads strict TOML configuration from built-in defaults, user defaults, repository `archy.toml`, an explicit `--config` file, and command-line state-root overrides. Later sources take precedence. Unknown keys are rejected.

Here is a small starting point:

```toml
schema_version = 1

[[layers]]
name = "Api"
include = ["src/**/Api/**"]
may_depend_on = ["Application", "Shared"]

[[layers]]
name = "Application"
include = ["src/**/Application/**"]
may_depend_on = ["Domain", "Shared"]

[enforcement]
hard_edge_kinds = ["calls", "references", "inherits"]
```

Only configured hard edge kinds with confidence exactly `1.0` can block verification. Use `archy config show --path . --json` before scanning to inspect the resolved, secret-free configuration. See the full [configuration reference](docs/configuration.md) for LSP profiles, providers, sidecars, scope, health weights, baselines, and exceptions.

## Use with Codex

Archy’s plugin is in [`plugins/archy`](plugins/archy). Put `archy` on `PATH`, then install the plugin through your Codex marketplace configuration. It starts local stdio MCP for the current repository and provides lifecycle hooks.

The typical loop is:

1. Start an attributed architecture session.
2. Ask MCP to check a proposed boundary-sensitive change or inspect dependents/rules.
3. Make the edit.
4. Let the post-tool hook report deterministic violations and advisory context.
5. Record an accepted/ignored/modified decision with a reason when needed.
6. End the session; Archy persists replayable events and queues summary work without blocking task completion.

For a non-Codex integration, start stdio MCP directly:

```sh
archy mcp stdio /absolute/path/to/repository
```

Optional MCP-over-HTTP remains loopback-only and requires an explicit bearer token:

```sh
archy mcp http --port 8789 --token <at-least-24-character-token> /absolute/path/to/repository
```

Read the [Codex integration guide](docs/integrations/codex.md) and [enforcement semantics](docs/architecture/enforcement.md) before enabling delivery gates.

## AI summaries and privacy

Model-backed summaries are optional. Set the model provider in configuration and provide an API key only through the environment:

```sh
export OPENAI_API_KEY='…'
```

The local capability view reports whether the model provider is available without exposing the key. If the provider is unavailable, Archy reports degraded coverage and defers summary work; it does not block your task or misrepresent the graph as fully documented. Review the repository’s AI consent/redaction settings before enabling source sharing.

## Backups, updates, and recovery

Treat the architecture database as durable local state:

```sh
archy db check --path .
archy db backup --path .
archy db diagnostics --path .
```

Before upgrading, verify the archive checksum and make a backup. Archy migration catalog and schema compatibility checks fail closed instead of silently rewriting incompatible state. See [security and release operations](docs/security-and-releases.md) for rollback and sidecar guidance.

## Development and verification

```sh
zsh scripts/test.sh
cd ui/archy-web && npm ci && npm test && npm run build
dotnet publish src/Archy/Archy.csproj --configuration Release --runtime osx-arm64 --self-contained true --no-restore
```

The repository’s verification suite covers architecture analysis, graph persistence/traversal, MCP and hook behavior, WebSocket replay, local API contracts/security, Native AOT publishing, and the web client’s API/accessibility contracts.

## Important limitations

- Initial product support is C# and macOS. Language profiles are extensible; Linux packaging is a follow-on target.
- One Archy workspace graph belongs to one Git repository. Cross-repository graphs are intentionally out of scope.
- Hook enforcement occurs after a tool operation. It can stop continuation and provide remediation, but cannot prevent or roll back an already completed write.
- Advisory findings retain confidence and evidence, but must not be treated as hard architectural rules.
- Optional Node/Python sidecars require their respective runtimes. Their absence is surfaced as a degraded capability.

## Further reading

- [User guide](docs/user-guide.md)
- [Configuration reference](docs/configuration.md)
- [Codex integration](docs/integrations/codex.md)
- [Security and release operations](docs/security-and-releases.md)
- [Extension guide](docs/extending-archy.md)
- [Architecture contracts](docs/architecture/contract-checklist.md)
- [Release acceptance checklist](docs/release-acceptance-checklist.md)
- [Contributing](CONTRIBUTING.md) · [Security policy](SECURITY.md)
