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

[language_servers.csharp]
command = "csharp-ls"
args = ["--stdio"]

[providers]
dependency_injection = true
messaging = true
entity_framework_core = true
cache = true
configuration = true

[[layers]]
name = "Api"
include = ["src/**/Api/**"]
may_depend_on = ["Application", "Shared"]

[[layers]]
name = "Application"
include = ["src/**/Application/**"]
may_depend_on = ["Domain", "Shared"]

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

The initial release accepts only the `csharp` language-server entry. The configuration model is deliberately separate from source-language adapters so later language support can add a validated entry rather than relying on untyped plugin settings.

`storage.state_root` may be relative; Archy resolves it against the configuration file that declared it. For example, `workspaces` in `~/.archy/config.toml` resolves to `~/.archy/workspaces`. Repository configuration cannot set it. `workspace init` honors resolved user/explicit state roots, and `--state-root` always wins.

OpenAI credentials must not be placed in TOML. Archy rejects `api_key`, `openai_api_key`, and `secret` fields. The OpenAI provider’s secure environment/Keychain resolution is implemented as a later dedicated slice; until then configuration may select `provider = "disabled"`.
