# Capability matrix

| Capability | Initial-release status | Enforcement/guarantee |
|---|---|---|
| Workspace discovery, state, config, lock | Implemented foundation | One Git root; macOS lock semantics |
| SQLite migrations and analysis-run provenance | Implemented foundation | Transactional, revision-ready |
| C# source/LSP facts and framework providers | Planned | Static evidence only; unresolved dynamic behavior shown |
| Graph revisions and dependency queries | Planned | Immutable, provenance-bearing facts |
| Deterministic rule verification | Planned | Hard at commit/CI once configured |
| Duplicates, placement, cohesion | Planned | Explainable advisory only |
| OpenAI summaries/embeddings | Planned | Opt-in, user-keyed, budgeted |
| MCP tools and Codex hooks | Planned | Post-edit guidance; no pre-write claim |
| macOS | Implemented host target | Native AOT publish tested |
| Linux | Planned next platform | No current release claim |
| Other source languages | Future-language | Requires an adapter, not just config |
