# Archy

**Architecture memory and deterministic delivery checks for C#, JavaScript, and TypeScript repositories.**

Archy analyzes one Git repository at a time, stores its versioned architecture graph outside the worktree, and gives developers and Codex concrete answers to questions such as:

- What depends on this type, module, or boundary?
- Would this change introduce a rule violation or dependency cycle?
- Where should a new capability live, and what evidence supports that answer?
- Which decisions, summaries, and health signals apply to this area of the codebase?

Archy is a local .NET 10 Native AOT application. Version `0.2.1` supports macOS on Apple Silicon and Intel. C# analysis is built in; JavaScript, JSX, TypeScript, and TSX semantic analysis uses the built-in standard-LSP profiles when `typescript-language-server` and `typescript` are available on `PATH`. It runs without a globally installed .NET runtime after release installation.

> Archy separates facts from advice. Only configured, deterministic, confidence-`1.0` graph facts can fail `archy verify`. Summaries, duplicate findings, placement suggestions, and health scores remain evidence-backed advisories.

## Start here

Choose the path that matches what you want to do:

| Goal | Start with |
| --- | --- |
| Install Archy on a Mac | [Install a release](#install-a-release-macos) |
| Map and verify one repository | [Set up a repository](#set-up-a-repository) |
| Use architecture context from Codex | [Connect Codex](#connect-codex-plugin-mcp-and-hooks) |
| Make architecture checks a merge gate | [Enforce delivery](#enforce-delivery-local-hooks-and-ci) |
| Browse a graph locally | [Use the local web app](#use-the-local-web-app) |
| Find existing code before writing new code | [Use semantic similarity](#use-semantic-similarity-optional-embeddings) |

## What Archy does today

- Builds a revisioned C# graph and standard-LSP semantic graphs for JavaScript, JSX, TypeScript, and TSX, with source locations, confidence, provenance, evidence, and bounded dependency traversal.
- Enforces configured layer coverage, illegal deterministic dependency directions, and deterministic cycles locally or in CI; exports SARIF for code scanning.
- Serves a local React/Tailwind graph explorer with nodes, edges, evidence, blast radius, session replay, health, duplicate, and placement views.
- Understands focused deterministic questions: `what uses …?`, `what does … use?`, and `what breaks if I delete …?`.
- Provides MCP preflight tools for rules, dependents, placement, duplicates, semantic similarity, sessions, decisions, and summary flushing, to Codex and Claude Code alike.
- Resolves a plain type, member, or path name into graph stable identifiers with `resolve_symbol`, so a caller never has to construct one by hand.
- Mirrors every tool's `structuredContent` payload into its `content` text block, so a client that reads only `content` still receives the answer rather than a count.
- Adds opt-in Codex lifecycle guidance: session context, post-tool checks after `Bash`, `Edit`, or `Write`, and session finalization.
- Keeps graph, decision, session, summary, backup, and diagnostic state outside the repository.

## Install a release (macOS)

Download the archive matching your Mac, verify its checksum, and run the included installer. `arm64` is Apple Silicon; `x64` is Intel.

```sh
ARCHY_VERSION=0.2.1

case "$(uname -m)" in
  arm64) ARCHY_ARCH=arm64 ;;
  x86_64) ARCHY_ARCH=x64 ;;
  *) echo "Archy does not publish a macOS release for $(uname -m)." >&2; exit 1 ;;
esac

ARCHIVE="archy-${ARCHY_VERSION}-osx-${ARCHY_ARCH}.tar.gz"
BASE_URL="https://github.com/polatefekaya/archy/releases/download/v${ARCHY_VERSION}"

curl --fail --location --remote-name "${BASE_URL}/${ARCHIVE}"
curl --fail --location --remote-name "${BASE_URL}/${ARCHIVE}.sha256"
shasum -a 256 -c "${ARCHIVE}.sha256"
tar -xzf "$ARCHIVE"
"./archy-${ARCHY_VERSION}-osx-${ARCHY_ARCH}/install.sh"
```

The installer verifies every bundled file before writing to `${ARCHY_INSTALL_PREFIX:-$HOME/.local}`. Add its `bin` directory to your shell startup file once, then verify the result:

```sh
echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.zprofile
export PATH="$HOME/.local/bin:$PATH"
archy --version
```

To upgrade, back up each important workspace with `archy db backup --path <repository>`, then repeat this process with a later reviewed release. To uninstall, run `uninstall.sh` from the same verified extracted archive.

## Set up a repository

Run Archy inside exactly one Git repository. Start by declaring the architecture you want it to enforce. The example below must be adapted to your real folder layout; every in-scope C# source file must match exactly one layer before strict verification can pass.

```toml
# archy.toml
schema_version = 1

[[layers]]
name = "Api"
include = ["src/**/Api/**"]
may_depend_on = ["Application", "Shared"]

[[layers]]
name = "Application"
include = ["src/**/Application/**"]
may_depend_on = ["Domain", "Shared"]

[[layers]]
name = "Domain"
include = ["src/**/Domain/**"]
may_depend_on = ["Shared"]

[[layers]]
name = "Shared"
include = ["src/**/Shared/**"]
may_depend_on = []

[enforcement]
hard_edge_kinds = ["calls", "references", "inherits"]
```

Then initialize, analyze, and verify:

```sh
cd /path/to/your/repository
archy config show --path . --json
archy workspace init --path .
archy analyze --path .
archy verify --path .
```

`workspace init` finds the owning Git repository and creates Archy state outside it. `analyze` creates or updates a graph revision. `verify` refreshes analysis and returns a non-zero result only for introduced deterministic findings. If you are adopting Archy in a repository with reviewed existing debt, use `archy baseline accept --path .`, inspect the generated `archy.baseline.json`, and commit it with `archy.toml`; never create a baseline merely to silence unknown findings.

Archy automatically inventories `.js`, `.mjs`, `.cjs`, `.jsx`, `.ts`, `.mts`, `.cts`, and `.tsx` sources. Install `typescript` and `typescript-language-server` when you want semantic symbols, references, and outgoing-call facts for those files; without that executable Archy reports explicit degraded semantic coverage instead of inventing facts.

See the [configuration reference](docs/configuration.md) for the built-in language-server profiles, provider patterns, scope, sidecars, baselines, and time-bounded exceptions.

## Connect Codex: plugin, MCP, and hooks

Archy’s Codex plugin starts `archy mcp stdio` for the current repository and contributes hooks. The plugin contains no API keys or source code; it requires the installed `archy` executable to be on `PATH`.

### Install from the public Git marketplace

```sh
codex plugin marketplace add polatefekaya/archy --ref v0.2.1
codex plugin add archy@archy
```

Start a **new Codex task** after installation. Confirm the integration before depending on it:

```sh
codex plugin list
codex mcp list
```

You should see `archy@archy` as installed and enabled, and an enabled `archy` MCP server running `archy mcp stdio`.

Open `/hooks` in Codex, review, and trust Archy’s hooks:

- `SessionStart` loads a bounded architecture and rule summary.
- `PostToolUse` runs after `Bash`, `Edit`, or `Write`; an introduced deterministic finding can stop the current agent turn with remediation.
- `Stop` finalizes session state.

These hooks are feedback after an operation. They cannot prevent or roll back a completed write, and they do not replace local Git hooks or CI.

### Use Archy in a Codex task

Ask Codex naturally, or explicitly mention Archy:

```text
Use Archy to check the dependency rules and dependents before changing this API boundary.
```

```text
Use Archy to suggest the correct placement for this new capability and explain the evidence.
```

For a non-Codex MCP client, use stdio directly:

```sh
archy mcp stdio /absolute/path/to/repository
```

This setup deliberately pins the plugin to a reviewed release tag. To move to a later release, remove the installed plugin and marketplace, replace the currently pinned tag with the new tag, then install again:

```sh
codex plugin remove archy@archy
codex plugin marketplace remove archy
codex plugin marketplace add polatefekaya/archy --ref vX.Y.Z
codex plugin add archy@archy
```

## Enforce delivery: local hooks and CI

Codex is helpful feedback; the delivery boundary is local Git hooks plus a required CI status check.

### Local Git gate

After `archy verify` is working in a repository:

```sh
archy hooks install --path .
archy hooks status --path .
```

Archy installs reversible `pre-commit` and `pre-push` wrappers. They evaluate staged or pushed Git trees in isolated temporary workspaces, not your mutable checkout. Developers can bypass local hooks, so CI remains mandatory.

### GitHub Actions gate

Copy [the workflow template](.github/workflow-templates/archy-verify.yml) into the target repository as `.github/workflows/archy-verify.yml`. In that repository’s Actions variables, set:

| Variable | Value |
| --- | --- |
| `ARCHY_VERSION` | The exact reviewed release version, for example `0.1.0`. |
| `ARCHY_SHA256` | The checksum from `archy-<version>-osx-arm64.tar.gz.sha256`. |

Run the workflow once so GitHub discovers **`Archy / verify`**, then protect the default branch: require that check, require it to be up to date, restrict bypass/direct pushes, and protect `archy.toml`, `archy.baseline.json`, `archy.exceptions.json`, and workflow files with code-owner review.

The complete policy, SARIF, and branch-protection instructions are in the [GitHub Actions guide](docs/ci/github-actions.md).

## Use the local web app

After initialization and analysis:

```sh
archy web serve --path .
```

Open [http://127.0.0.1:8788](http://127.0.0.1:8788). The server is loopback-only. Use the map to explore nodes and dependencies; use the detail and evidence views to understand why an edge exists before acting on it.

## Daily workflow

1. Pull the latest repository changes.
2. Run `archy analyze --path .` when you need a fresh graph.
3. Ask Archy/Codex for dependents, rules, and placement before boundary-sensitive work.
4. Make the change and address any post-tool hook stop.
5. Run `archy verify --path .` before committing.
6. Let the local Git gate and required `Archy / verify` CI check protect delivery.

## Diagnose readiness without changing state

`archy doctor --path . --json` reports repository, effective configuration, existing local state/database, active graph, language-profile readiness, MCP availability, and embedding configuration. It is read-only: it never initializes a workspace, migrates a database, launches an LSP, runs analysis, or sends a model request. Exit code `2` means at least one blocking readiness error; warnings remain advisory.

Use `archy doctor list --path . --json` to inspect every configured language profile, including profiles with no matching source files or unavailable executables. This is the quickest way to diagnose a missing marker, source inventory, or language-server command.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| `archy: command not found` | Ensure `$HOME/.local/bin` is on `PATH`, open a new terminal, then run `archy --version`. |
| Codex has no Archy tools | Run `codex plugin list` and `codex mcp list`; confirm the plugin is enabled, start a new task, and ensure `archy` is on `PATH`. |
| Hooks do not run | Open `/hooks`, review/trust the Archy hooks, and confirm the project is trusted by Codex. |
| `workspace has not been initialized` | Run `archy workspace init --path .` from the target Git repository. |
| `doctor` reports `languages.<id>` | Run `archy doctor list --path . --json`; install/configure its command only when matching in-scope sources require semantic coverage. |
| `verify` reports no configured layer rule | Add a reviewed `archy.toml` with at least one layer and run `archy config show --path . --json`. |
| Layer coverage fails | Adjust layer globs until every in-scope C# source node matches exactly one layer. |
| A sidecar or language server is unavailable | Install its declared runtime/executable; Archy reports this as degraded coverage, never as a successful semantic analysis. |
| Native AOT archive does not run on Linux | Linux release artifacts are not published yet; macOS is the supported initial platform. |

## Backups, recovery, and privacy

Archy state is durable local data. Back up before upgrades or experiments:

```sh
archy db check --path .
archy db backup --path .
archy db diagnostics --path .
```

Restore only from an explicit backup after stopping Archy. Schema and migration compatibility checks fail closed rather than silently rewriting incompatible state.

Optional AI-assisted features must never receive credentials through `archy.toml`. Keep keys in the environment or supported secure storage; review consent and redaction configuration before authorizing any source sharing. A model, LSP, or sidecar outage is shown as degraded coverage rather than a false architectural pass.

### Use semantic similarity (optional embeddings)

Before creating a new handler, service, module, or abstraction, ask Archy to look for existing code with similar names, signatures, and dependency neighborhoods. The read-only `find_similar` MCP tool accepts any one of:

- `query`: a natural-language intent;
- `sourceStableId`: an existing persisted declaration;
- `sourceFilePath`: a repository-relative source file; or
- `sourceCode`: a bounded pasted code snippet.

It returns ranked candidates with an explanation for every score component:

- `symbol`: normalized query/name/canonical-identity overlap;
- `structure`: persisted symbol-signature overlap;
- `dependency_neighborhood`: shared persisted dependencies when you provide an existing `sourceStableId`;
- `embedding`: cached cosine similarity, only when compatible vectors exist for the supplied model.

Use it from Codex with a prompt such as:

```text
Before implementing this, use find_similar to locate existing session lifecycle patterns.
Prefer reuse or extension when the evidence is strong; explain why a new abstraction is necessary otherwise.
```

For files and symbols, Archy resolves the corresponding persisted declaration automatically, so callers do not need to manually find a stable ID. Without indexed vectors it still returns deterministic graph evidence and explicitly marks semantic evidence as unavailable—this is not a claim that no semantically similar code exists.

### Opt-in embedding index

Embedding generation is disabled until the repository explicitly permits source sharing. Set `OPENAI_API_KEY` in your environment, then run the guided command:

```sh
export OPENAI_API_KEY="..."
archy embeddings setup --allow-source-sharing --path .
archy analyze --path .
archy embeddings index --path .
```

`setup` records `provider = "openai"`, `embedding_model = "text-embedding-3-large"`, and `source_sharing = "summaries_and_embeddings"` in the repository configuration. It refuses to write anything without both the environment key and the explicit `--allow-source-sharing` flag. Do not put credentials in `archy.toml`.

To configure it manually instead:

```toml
[model]
provider = "openai"
embedding_model = "text-embedding-3-large"
max_requests_per_run = 30
max_tokens_per_run = 200000

[memory]
source_sharing = "summaries_and_embeddings"
```

Set `OPENAI_API_KEY` in your shell or secret manager. Index only after analysis has produced an active graph:

```sh
archy embeddings index --path . --dry-run
archy embeddings index --path .
archy embeddings status --path . --json
```

`--dry-run` performs no provider request and writes no cache rows; use it to inspect the bounded method-chunk plan before any source leaves your machine. `status` shows cache count, dimensions, latest cache timestamp, effective provider, and consent state without exposing vectors. Generated vectors are stored in Archy’s machine-local state, never in repository files.

After indexing, `find_similar` automatically uses the configured embedding model for symbol and file inputs when compatible cached vectors exist. For an intent query, pasted snippet, or file, it may create one bounded query vector and cache it locally—but only after the repository has opted into `summaries_and_embeddings`; otherwise it stays entirely structural. You can still supply `embeddingModel` to select a particular cache. The MCP tool does not require or accept API keys.

## Important limits

- One graph belongs to one Git repository; cross-repository graphs are intentionally out of scope.
- C# syntax/framework analysis and macOS are the initial supported surface. JavaScript, JSX, TypeScript, and TSX have built-in standard-LSP semantic profiles; language-specific framework providers remain future work.
- Only exact-confidence deterministic facts can block. Advice always retains evidence and confidence.
- Post-tool hooks happen after the operation they inspect.
- Node and Python are required only for their respective optional sidecars.

## Further reading

- [User guide](docs/user-guide.md)
- [Configuration reference](docs/configuration.md)
- [Codex integration](docs/integrations/codex.md)
- [Deterministic enforcement](docs/architecture/enforcement.md)
- [GitHub Actions integration](docs/ci/github-actions.md)
- [Security and release operations](docs/security-and-releases.md)
- [Extension guide](docs/extending-archy.md)
- [Architecture contracts](docs/architecture/contract-checklist.md)
- [Release acceptance checklist](docs/release-acceptance-checklist.md)
- [Contributing](CONTRIBUTING.md) · [Security policy](SECURITY.md)
