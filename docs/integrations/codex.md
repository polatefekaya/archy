# Codex integration

Archy's Codex package lives at [`plugins/archy`](../../plugins/archy). It starts the local stdio MCP server and adds lifecycle hooks. Install only after the Native AOT `archy` executable is available on `PATH`; the package never carries secrets.

The MCP server is per repository. Its tool set includes dependency/rule/duplicate/placement preflight, decision recording and retrieval, plus session and summary-flush control. The optional HTTP transport binds only to loopback and requires an explicit bearer token; stdio is the default.

Codex hooks are guidance around a completed operation. `SessionStart` adds a bounded graph/rule summary. `PostToolUse` covers `Bash|Edit|Write`, then can return a turn stop for an introduced deterministic verification finding. It is not a pre-write guard and cannot protect arbitrary local file writes.

Use this flow:

1. Query Archy before a boundary-sensitive edit.
2. Address any post-edit turn stop and run `archy verify`.
3. Deliver through the installed Git gate and required `Archy / verify` CI check.

See [enforcement semantics](../architecture/enforcement.md) for bypass limits and the required branch-protection setup.
