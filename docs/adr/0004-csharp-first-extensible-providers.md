# ADR 0004: C# first, language-independent provider contracts

## Decision

The initial release analyzes C#/.NET repositories. Provider contracts are language-independent, but a new language requires a parser/LSP adapter, source-range model, and language-specific string normalization; configuration alone is not language support.

## Consequences

The initial configuration validates only `language_servers.csharp`. C# provider coverage reports explicit static facts and exposes unsupported dynamic behavior as unresolved rather than guessed.
