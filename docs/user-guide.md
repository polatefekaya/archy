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

## What Archy guarantees

Strict verification is limited to configured hard edge kinds with confidence `1.0`. It produces a non-zero verification outcome for introduced, deterministic violations. Baselines and exceptions are explicit repository policy artifacts and never permit an expired exception.

Duplicate detection, placement advice, model summaries, health scoring, and natural-language query assistance are advisory. They always retain their evidence and confidence, and unsupported deterministic queries request clarification rather than inventing an answer.

## Model, privacy, and recovery

The default model provider is OpenAI Responses API, enabled only when the user supplies `OPENAI_API_KEY`. Archy sends only source material that its consent and redaction policy authorizes for a requested summary. The local capability view reports model readiness without returning the key.

Workspace state, including graph revisions, summaries, decisions, and sessions, lives outside the repository. Back up before an upgrade with `archy db backup`; use `archy db check` after recovery and `archy db restore` only with an explicit backup path. A newer unsupported database schema fails closed rather than being silently rewritten.

## Limitations

Initial language support is C#. LSP profiles are configuration driven so other standard-LSP languages can be added without host-specific registrations, but a profile must declare its executable, initialization contract, source extension set, and semantic limits. A missing profile, unavailable LSP, model outage, or sidecar outage is surfaced as degraded coverage—not inferred as healthy architecture.
