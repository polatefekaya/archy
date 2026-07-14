# ADR 0003: One transactional SQLite source of truth

## Decision

Graph facts, revisions, analysis runs, decisions, summaries, duplicate lifecycle, and health components share one per-workspace SQLite database.

## Consequences

The database is migrated under an exclusive workspace lease. Graph revisions are immutable; mutable operational rows retain append-only event history where replay needs it. Feature code does not create isolated databases.

## Rejected alternatives

Separate feature stores would make revision replay, calibration, health scoring, and explainability inconsistent.
