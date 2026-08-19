# Archy Codex plugin

This package starts Archy's local stdio MCP server and adds opt-in `SessionStart`, `PostToolUse`, and `Stop` hooks. It contains no secrets. The `archy` Native AOT executable must be on `PATH` before enabling the plugin.

Install from a repository marketplace by placing the contents of `marketplace.example.json` at `.agents/plugins/marketplace.json`, then restart Codex and install **Archy**. Review and trust the plugin hook before it runs.

The plugin provides 22 MCP tools for graph/rule queries, similarity and reuse guidance, planning, readiness, decisions, and sessions. Their descriptions and JSON input schemas are published through MCP `tools/list`. The hooks and advisory tools do not claim to prevent a file write. Use `archy verify` and required CI for delivery-boundary enforcement.
