# Data handling, privacy, and recovery

Local state may contain repository-relative paths, source ranges, graph facts, hashes, rule outcomes, decisions, summaries, embeddings, and model-request metadata. It must never contain OpenAI keys or unredacted secret values.

Graph revisions, summary versions, decisions, sessions, session events, duplicate observations, cluster revisions, and health snapshots are durable provenance. Summary, decision, event, duplicate-observation, cluster-revision, and health-snapshot rows are append-only; a session end is represented by a final ordered event rather than overwriting its start record. Summary identities retain only a pointer to their newest immutable version.

Source fragments leave the machine only for an explicitly enabled model operation after redaction. Full source and repository archives are never default model input. Telemetry is off by default.

| Failure | Required behavior |
|---|---|
| Interrupted migration | Transaction rolls back; next startup retries safely |
| SQLite contention | Bounded wait then actionable conflict; no corruption |
| Corrupt database | Preserve file, mark unhealthy, require explicit repair |
| LSP/sidecar unavailable | Degraded coverage; no invented facts |
| Model failure | Deterministic analysis remains valid; semantic work stale/pending |
| Interrupted analysis | Failed/cancelled run event; no graph revision |

No destructive repair, cache eviction, or state deletion is automatic.
