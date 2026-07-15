# ADR 0004: C# first, language-independent provider contracts

## Decision

The initial release's syntax and framework providers analyze C#/.NET repositories. Standard-LSP declaration-symbol analysis is language-independent: a new language requires a declarative profile, not a host adapter. Framework-specific syntax enrichment remains a separate provider concern.

## Consequences

The configuration validates `language_server_profiles` uniformly. C# provider coverage reports explicit static facts and exposes unsupported dynamic behavior as unresolved rather than guessed.
