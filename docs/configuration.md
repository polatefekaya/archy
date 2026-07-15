# Archy configuration

`archy.toml` is a strict, versioned TOML 1.1 file. Every file must begin with `schema_version = 1`; unknown keys are rejected rather than silently ignored. That keeps rule enforcement and analysis coverage auditable.

Archy loads configuration in this order, with later values taking precedence:

1. Built-in C#/.NET defaults.
2. User defaults at `~/.archy/config.toml`.
3. Repository configuration at `<repository-root>/archy.toml`.
4. A command-specific `--config <path>` file.
5. A command-line `--state-root <path>` override.

Inspect the final result with:

```bash
archy config show --path . --json
```

The command only emits the typed configuration accepted by this schema; API keys are not a schema field and are never printed.

## Version 1 schema

```toml
schema_version = 1

[workspace]
display_name = "Payments"

# This may appear in user defaults or an explicitly selected config. It is
# rejected in a repository's archy.toml so an untrusted worktree cannot redirect
# Archy's local writes.
[storage]
state_root = "workspaces"

[[language_server_profiles]]
id = "csharp"
language_id = "csharp"
extensions = [".cs"]
markers = ["*.sln", "*.slnx", "*.csproj"]
command = "/opt/archy/roslyn/Microsoft.CodeAnalysis.LanguageServer"
args = ["--stdio"]
symbol_identity_prefix = "csharp"
# Bound each relationship capability independently. Must be 1 through 100000.
max_symbol_queries = 10000

[language_server_profiles.symbol_kinds]
namespace = [3]
type = [5, 10, 11, 23]
method = [6, 9, 12]
property = [7]
field = [8]
event = [24]
parameter = [26]

[providers]
dependency_injection = true
messaging = true
entity_framework_core = true
cache = true
configuration = true

[[provider_patterns]]
id = "custom-domain-publish"
framework = "MyCompany.Messaging"
match_kind = "invocation" # invocation, type, or attribute
member = "Publish"
type = "MyCompany.Messaging.IDomainBus" # optional textual type constraint
capture_name = "message"
capture_argument_index = 0

[[layers]]
name = "Api"
include = ["src/**/Api/**"]
may_depend_on = ["Application", "Shared"]

[[layers]]
name = "Application"
include = ["src/**/Application/**"]
may_depend_on = ["Domain", "Shared"]

# Only these edge kinds may block verification, and only when their graph
# confidence is exactly 1.0. Lower-confidence provider edges remain advisory.
[enforcement]
hard_edge_kinds = ["calls", "references", "inherits"]

[model]
provider = "openai" # or "disabled"
summary_model = "gpt-5"
embedding_model = "text-embedding-3-large"
max_requests_per_run = 30
max_tokens_per_run = 200000

[sidecars]
jscpd_command = "jscpd"
louvain_command = "python3"

[scope]
include = ["src/**/*.cs"]
exclude = ["**/bin/**", "**/obj/**"]

[health_weights]
architecture = 0.40
duplicates = 0.20
documentation = 0.20
decisions = 0.20
```

`language_server_profiles` is the extension point for every standard-LSP language. A profile declares the language ID sent in `didOpen`, source extensions, activation markers, executable, argument array, canonical-identity prefix, LSP `SymbolKind` mapping, and a bounded `max_symbol_queries` limit. The limit applies independently to reference collection and outgoing-call collection, preventing an unexpectedly large snapshot from causing an unbounded number of server requests. Profiles upsert by `id` across configuration layers: a repository can add `typescript` without removing the built-in `csharp` profile, or replace the `csharp` profile to pin a server binary. No host registration, reflection, or dynamic plugin loading is involved.

Each `layers.include` value is a unique repository-relative glob; a node must match exactly one layer before it can participate in strict verification. `may_depend_on` may reference another declared layer, including one declared later in the file, but never the layer itself. `enforcement.hard_edge_kinds` is also strict: identifiers are lowercase and unique. Archy still independently requires an edge confidence of exactly `1.0` before it can block verification, so adding a provider edge kind to configuration cannot turn lower-confidence string/pattern evidence into a hard failure.

Architecture debt accepted during adoption is stored in the repository-root `archy.baseline.json`, not in `archy.toml` or machine-local state. Create or deliberately replace it with `archy baseline accept`; commit it alongside the rules it fingerprints. The file is strict JSON and contains only stable finding identities, rule fingerprint, graph revision, and evidence targets—never credentials or local paths. Changing layer rules or hard-edge kinds makes the old fingerprint incompatible, so the next verification fails current findings until an explicit new baseline is accepted.

Temporary architecture exceptions live in the separate committed `archy.exceptions.json` policy artifact. Create one only with `archy exception accept --finding <exact-key> --author <author> --reason <reason> --review-at <ISO-8601> --expires-at <ISO-8601>`. Each append-only record is tied to one exact currently introduced finding and must include review and expiry timestamps. There are no glob, layer-wide, edge-kind-wide, or directory-wide exception fields; expiry fails closed, so an expired exception is reported but does not suppress verification.

For CI code-scanning integrations, use `archy verify --sarif --output <report-path>`. SARIF output is a report format only: it does not change the enforcement exit-code contract or create/update either policy artifact.

For every matching profile, Archy requires matching source documents and, when configured, at least one marker. It resolves the configured command on `PATH` (or as a configured path), then performs the versioned LSP initialize/capability handshake. A missing executable, missing marker, failed synchronization, or missing capability yields an explicit unavailable/degraded batch and no semantic graph facts. A profile never falls back to a different language server.

`storage.state_root` may be relative; Archy resolves it against the configuration file that declared it. For example, `workspaces` in `~/.archy/config.toml` resolves to `~/.archy/workspaces`. Repository configuration cannot set it. `workspace init` honors resolved user/explicit state roots, and `--state-root` always wins.

OpenAI credentials must not be placed in TOML. Archy rejects `api_key`, `openai_api_key`, and `secret` fields. The OpenAI provider’s secure environment/Keychain resolution is implemented as a later dedicated slice; until then configuration may select `provider = "disabled"`.
