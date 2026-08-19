# Archy

Archy reads one Git repository, builds a versioned graph of what depends on what, and keeps that graph outside your worktree. You query it before you write code, and you can turn it into a merge gate afterwards.

It exists to answer the questions that are expensive to get wrong. What breaks if I delete this type. Does this change cross a boundary it is not allowed to cross. Where does this new capability belong. Is there already code that does the same thing.

Archy is a local .NET 10 binary that needs no installed runtime once unpacked. Version `0.2.2` runs on macOS, on Apple Silicon and Intel. C# analysis is built in. JavaScript, JSX, TypeScript and TSX go through standard LSP profiles when `typescript-language-server` is on your `PATH`.

## What it looks like

Once a repository is set up, the gate is one command.

```
$ archy verify --path .
╭───────────────────────────────────────╮
│ ARCHY  ·  ARCHITECTURE VERIFIED       │
│ No introduced deterministic findings. │
│ Graph revision  115                   │
│ Baseline  missing                     │
│ Introduced findings  0                │
│ Active exceptions  0                  │
├───────────────────────────────────────┤
│ Architecture gate passed.             │
╰───────────────────────────────────────╯
```

On a 48k line codebase that run takes roughly two tenths of a second, because analysis is keyed on content hashes and skips everything that did not change. Exit code is 0, so CI can use it directly.

When something is actually wrong you get the finding and a non-zero exit.

```
$ archy verify --path .
╭────────────────────────────────────────────────────────────────────────╮
│ ARCHY  ·  ARCHITECTURE NEEDS ATTENTION                                 │
│ Introduced findings require review before delivery.                    │
│ Graph revision  9                                                      │
│ Introduced findings  1                                                 │
├────────────────────────────────────────────────────────────────────────┤
│ Inspect findings, accept only narrow justified exceptions, then retry. │
╰────────────────────────────────────────────────────────────────────────╯

introduced EnforcementUnavailable: No graph edge satisfies the configured
hard-edge policy, so no layer violation or dependency cycle can be reported.
Enforcement reads only confidence-1.0 edges of kind: calls, inherits,
references. This revision has 4 edge(s) of kind: declares, using. Semantic
analysis is usually missing: run 'archy doctor --path .' and install the
language server its blocking check names, then re-run 'archy analyze'.
```

That finding deserves a note, because it is the difference between a gate and a decoration. Layer rules are enforced only through edges Archy is certain about. A `using` directive is recorded at confidence 0.65 and never enforces anything, since importing a namespace does not prove you depend on it. So if semantic analysis was unavailable, the graph can end up holding nothing enforceable at all, and every run passes no matter what the code does. Archy reports that instead of printing a green box.

## The rule Archy holds itself to

Only configured, deterministic, confidence-1.0 graph facts can fail `archy verify`. Summaries, duplicate findings, placement suggestions and health scores stay advisory, and they carry their evidence with them so you can judge them yourself.

The same rule applies when a tool is missing. Without a language server Archy reports degraded coverage. It does not invent facts, and it does not quietly report success.

## Install

```sh
ARCHY_VERSION=0.2.2

case "$(uname -m)" in
  arm64) ARCHY_ARCH=arm64 ;;
  x86_64) ARCHY_ARCH=x64 ;;
  *) echo "No macOS release for $(uname -m)." >&2; exit 1 ;;
esac

ARCHIVE="archy-${ARCHY_VERSION}-osx-${ARCHY_ARCH}.tar.gz"
BASE_URL="https://github.com/polatefekaya/archy/releases/download/v${ARCHY_VERSION}"

curl --fail --location --remote-name "${BASE_URL}/${ARCHIVE}"
curl --fail --location --remote-name "${BASE_URL}/${ARCHIVE}.sha256"
shasum -a 256 -c "${ARCHIVE}.sha256"
tar -xzf "$ARCHIVE"
"./archy-${ARCHY_VERSION}-osx-${ARCHY_ARCH}/install.sh"
```

The installer checks every bundled file before writing to `${ARCHY_INSTALL_PREFIX:-$HOME/.local}`. Put its `bin` directory on your path once.

```sh
echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.zprofile
export PATH="$HOME/.local/bin:$PATH"
archy --version
```

To remove it later, run `uninstall.sh` from the same extracted archive.

## Set up a repository

Start by declaring the architecture you want enforced. Adapt the globs to your real folder layout, because every in-scope C# file has to match exactly one layer before strict verification will pass.

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

Then four commands.

```sh
cd /path/to/your/repository
archy workspace init --path .
archy doctor --path .
archy analyze --path .
archy verify --path .
```

`workspace init` locates the owning Git repository and creates state outside it. `doctor` is read only and tells you what is missing before you spend time on anything else. `analyze` builds a graph revision. `verify` refreshes analysis and returns non-zero only for findings you introduced.

Read the doctor output rather than skimming it. On a machine with no C# language server it says so directly.

```
$ archy doctor --path .
Doctor: 1 error(s), 5 warning(s), 10 info.
Info   analysis.graph      Active graph revision 115 has 2014 nodes.
Error  languages.csharp    Matching source files exist but the language-server
                           command is unavailable. Remediation: Install or
                           configure 'Microsoft.CodeAnalysis.LanguageServer'.
```

Exit code 2 means at least one blocking readiness error, and warnings stay advisory. `archy doctor list --path .` shows every configured language profile including the ones with no matching files, which is usually the fastest way to find a missing marker or a wrong command.

If you are adopting Archy in a repository that already carries debt, run `archy baseline accept --path .`, read the generated `archy.baseline.json`, and commit it next to `archy.toml`. Existing findings become legacy and only new ones block. Creating a baseline to silence findings you have not read defeats the point.

## Use it from a coding agent

Archy speaks MCP, so any client that speaks MCP can use it. Two have packaged plugins.

### Claude Code

```sh
claude plugin marketplace add polatefekaya/archy
claude plugin install archy@archy
```

Restart the session, then check `/plugin` and `/mcp`. You should see `archy` enabled and an `archy` server running `archy mcp stdio`.

The plugin ships 22 MCP tools, a skill that tells the agent when to reach for them, and three lifecycle hooks. `SessionStart` injects a short architecture summary. `PostToolUse` re-checks touched paths after `Bash`, `Edit`, `MultiEdit`, `NotebookEdit` and `Write`, and can stop the turn when a change introduces a finding. `Stop` finalizes session state. It costs about 100 tokens per session, since tool schemas resolve at call time rather than sitting in context.

### Codex

```sh
codex plugin marketplace add polatefekaya/archy --ref v0.2.2
codex plugin add archy@archy
```

Start a new task, confirm with `codex plugin list` and `codex mcp list`, then open `/hooks` and trust the Archy hooks.

### Any other MCP client

```sh
archy mcp stdio /absolute/path/to/repository
```

There is also `archy mcp http --port <port> --token <token>` when stdio does not fit.

### What the agent actually receives

Every tool returns a one line summary, then the full payload, and the same payload again in `structuredContent`. A client that only surfaces text still gets the answer rather than a count.

Ask for a symbol by name and you get real identifiers back instead of something the model guessed at.

```
resolve_symbol("UnifiedAdvisoryComposer")

Resolved 4 candidate(s) for "UnifiedAdvisoryComposer"; returning the first 3.
{"candidates":[
  {"stableId":"csharp:type:src/.../IUnifiedAdvisoryComposer.cs:...IUnifiedAdvisoryComposer",
   "nodeKind":"interface","filePath":"src/.../IUnifiedAdvisoryComposer.cs",
   "startLine":2,"provider":"csharp-syntax","confidence":1}, ...]}
```

Feed one of those into a graph query and every edge arrives with its kind and confidence attached.

```
get_dependents(stableId, depth: 2)

revision 113; edges 1
{"revision":113,"truncated":false,"edges":[
  {"source":"file:src/.../IUnifiedAdvisoryComposer.cs",
   "target":"csharp:type:src/.../IUnifiedAdvisoryComposer.cs:...",
   "kind":"declares","confidence":1}]}
```

Similarity results explain themselves, so the agent can tell a real match from a coincidence.

```
find_similar("compose unified advisories")

{"score":0.277,"filePath":"src/.../UnifiedAdvisoryComposer.cs","evidence":[
  {"kind":"symbol","reason":"Name and canonical identity share normalized tokens with the query."},
  {"kind":"structure","reason":"Persisted symbol signature shares normalized tokens with the query."}]}
```

Hooks are feedback after an operation. They cannot prevent or roll back a write that already landed, and they do not replace Git hooks or CI.

## Make it a gate

Agent hooks help while you work. The delivery boundary is local Git hooks plus a required CI check.

```sh
archy hooks install --path .
archy hooks status --path .
```

Archy installs reversible `pre-commit` and `pre-push` wrappers that evaluate the staged or pushed tree in a temporary workspace rather than your mutable checkout. Developers can skip local hooks, which is why CI is the part that actually holds.

For CI, copy [the workflow template](.github/workflow-templates/archy-verify.yml) into the target repository as `.github/workflows/archy-verify.yml`, then set two Actions variables. `ARCHY_VERSION` is the release you reviewed, for example `0.2.1`. `ARCHY_SHA256` is the checksum from that release's `.sha256` file. Run it once so GitHub discovers the `Archy / verify` check, then require it on your default branch.

`archy verify --sarif --output archy.sarif` writes SARIF for code scanning. Findings map to `ARCHY001` for layer coverage, `ARCHY002` for layer direction, `ARCHY003` for dependency cycles and `ARCHY004` for a gate that cannot fail.

The full policy and branch protection setup lives in the [GitHub Actions guide](docs/ci/github-actions.md).

## Other things it does

`archy web serve --path .` starts a loopback only graph explorer on port 8788, with nodes, edges, evidence, blast radius, session replay and health views.

`archy architecture explain --lookup <name|path|id>` prints what Archy knows about one thing, where each fact came from, and what it does not know.

```json
{"ResolvedStableId":"csharp:type:src/.../McpToolResponseComposer.cs:...",
 "Facts":[
   {"Kind":"source","Detail":"Defined at src/.../McpToolResponseComposer.cs:22.","Provenance":"graph_nodes"},
   {"Kind":"provider","Detail":"Provider 'csharp-syntax' reported confidence 1.","Provenance":"graph_nodes"}],
 "History":[{"Revision":113,"Detail":"Present from revision 113."}],
 "Unknowns":["No architecture decisions target this graph node."]}
```

`archy change plan`, `archy impact analyze` and `archy refactor plan` answer planning questions from the same graph. `archy similarity clusters` and `archy similarity reintroduced` report duplicate structure and code that came back after being deleted. `archy inventory` lists what is in scope. `archy exception accept` records a narrow dated exception for a single finding. `archy db check|backup|restore|vacuum|diagnostics` manages local state, and `archy db backup` is worth running before an upgrade.

`archy --help` prints the full surface.

## Semantic similarity is optional

Structural similarity works out of the box. Embeddings stay off until you explicitly allow source sharing, and no credential ever belongs in `archy.toml`.

```sh
export OPENAI_API_KEY="..."
archy embeddings setup --allow-source-sharing --path .
archy analyze --path .
archy embeddings index --path . --dry-run
archy embeddings index --path .
```

`setup` refuses to write anything without both the environment key and the explicit flag. `--dry-run` makes no provider request and writes no cache rows, so you can inspect the chunk plan before any source leaves the machine. Vectors live in machine local state, never in repository files. `archy embeddings status --path . --json` shows cache count, dimensions and consent state without exposing the vectors themselves.

Without indexed vectors `find_similar` still returns graph evidence and marks semantic evidence as unavailable. That is not a claim that nothing similar exists.

## Limits

One graph belongs to one Git repository. Cross repository graphs are out of scope on purpose.

macOS is the only published platform right now. Linux artifacts are not built yet, which matters because CI usually runs on Linux.

Layer enforcement needs semantic edges, and those need a working language server for the language in question. Without one, `archy verify` reports `ARCHY004` rather than passing.

Only exact confidence deterministic facts block. Everything else is advice with its evidence attached.

Post-tool hooks run after the operation they inspect, so they report rather than prevent.

Node and Python are needed only for the optional sidecars that use them.

## Further reading

[User guide](docs/user-guide.md) ·
[Configuration](docs/configuration.md) ·
[Deterministic enforcement](docs/architecture/enforcement.md) ·
[Codex integration](docs/integrations/codex.md) ·
[GitHub Actions](docs/ci/github-actions.md) ·
[Security and releases](docs/security-and-releases.md) ·
[Extending Archy](docs/extending-archy.md)

MIT licensed. Security policy in [SECURITY.md](SECURITY.md), contribution notes in [CONTRIBUTING.md](CONTRIBUTING.md).
