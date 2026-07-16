# Archy Codex plugin

This package starts Archy's local stdio MCP server and adds an opt-in SessionStart hook. It contains no secrets. The `archy` Native AOT executable must be on `PATH` before enabling the plugin.

Install from a repository marketplace by placing the contents of `marketplace.example.json` at `.agents/plugins/marketplace.json`, then restart Codex and install **Archy**. Review and trust the plugin hook before it runs.

The plugin intentionally provides post-start context and preflight tools; it does not claim to prevent a file write. Use `archy verify` and required CI for delivery-boundary enforcement.
