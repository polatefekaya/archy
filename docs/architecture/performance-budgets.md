# Initial performance budgets

Release gates are measured on deterministic C# fixtures: small 10k LOC, medium 100k LOC, large 1M LOC.

| Operation | Small | Medium | Large |
|---|---:|---:|---:|
| Cold parse/fact scan | 30s | 3m | 15m |
| One-file incremental analysis | 2s | 5s | 15s |
| Deterministic hook advisory | 250ms | 500ms | 1s |
| SQLite dependency traversal p95 | 100ms | 300ms | 1s |
| UI first graph view | 1s | 2s | 5s |
| Revision replay seek | 250ms | 1s | 3s |

Model latency is excluded. CI records p50/p95, fixture size, host class, and regression percentage.
