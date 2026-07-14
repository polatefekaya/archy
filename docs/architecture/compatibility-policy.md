# Compatibility policy

| Surface | Version rule | Breaking-change behavior |
|---|---|---|
| `archy.toml` | `schema_version` | Reject unsupported schema; provide migration guidance |
| SQLite | numbered transactional migrations | Newer DB refuses older binary |
| CLI JSON | `contract_version` before public stability | New required fields require a new major contract |
| MCP/hook/sidecar | explicit schema/protocol version | Host rejects incompatible required contracts |

Human CLI output is not machine-stable. Current internal JSON is not public-stable until it carries a contract version and fixture compatibility tests.
