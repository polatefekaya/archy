# ADR 0005: User-keyed OpenAI provider behind an abstraction

## Decision

AI summaries, embeddings, and semantic duplicate work use an `IModelProvider` abstraction. The initial provider uses the OpenAI Responses API with a user-supplied credential; Codex is an MCP client, never Archy's inference backend.

## Consequences

API keys are rejected from TOML and logs. Model calls require consent, redaction, structured responses, budgets, and durable request metadata. Deterministic graph/rule work remains functional when AI is disabled.
