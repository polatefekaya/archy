# Release acceptance checklist

No Archy release is complete solely because its feature list is implemented. The release owner records links to CI artifacts, test runs, and known limitations for every item below.

| Area | Acceptance evidence | Required release result |
| --- | --- | --- |
| Workspace and state | `workspace init`, database check, backup/restore integration tests | State is external to the repository and migration compatibility fails closed. |
| C# graph analysis | Cold and incremental scan fixture, revision/page/traversal API tests | Graph identity, provenance, and bounded traversal are revision-aware. |
| Enforcement | Introduced/legacy baseline tests, Codex post-tool hook fixture, CI SARIF | Only deterministic confidence-1.0 facts can block delivery; writes are not falsely claimed to be prevented. |
| Memory and AI | Consent/redaction/provider/failure-work tests | Model failure is deferred and surfaced; credentials and unauthorized source never appear in UI/API output. |
| Advisories | Duplicate, placement, health, and decision tests | Advisory confidence/evidence is shown separately from hard rules. |
| MCP and hooks | stdio/HTTP auth tests, session/replay/live-event tests | Tools and lifecycle hooks are discoverable, attributed, durable, and reconnect-safe. |
| Local UI | SPA production build, API contracts, keyboard/manual screen-reader acceptance | Empty, ready, degraded, and unavailable states are distinguishable; graph has textual alternatives. |
| Native distribution | arm64 and x64 `package-macos.sh`, checksum/install/uninstall smoke evidence | Archive verifies before install and runs without a global .NET runtime. |
| Security | dependency-audit artifact, local bind/auth/header tests | No high/critical production npm advisory; no default remote API exposure. |
| Recovery and scale | documented benchmark baseline and fault-injection report | Budgets are evaluated against approved baselines; failures preserve an actionable prior state. |
| Documentation and governance | user guide, extension guide, policies, issue/PR templates | Guarantees, advisory boundaries, privacy, sidecars, and support limits are current. |

## Sign-off

Record version, commit SHA, archive checksums, supported architectures, schema migration range, sidecar versions, known limitations, and the named release owner. Any unchecked item blocks a release or must have an explicit, time-bounded exception approved by the maintainer responsible for that area.
