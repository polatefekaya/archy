# Archy Claude Code plugin

This package starts Archy's local stdio MCP server for the current repository and adds opt-in
`SessionStart`, `PostToolUse`, and `Stop` hooks. It contains no secrets and no source code. The
`archy` Native AOT executable must be on `PATH` before enabling the plugin.

## Requirements

- `archy` on `PATH` (see the [install instructions](../../README.md#install-a-release-macos))
- An initialized workspace in the repository you open: `archy workspace init --path .`
- A committed `archy.toml` describing the layers you want enforced

Without an initialized workspace the hooks stay silent and the MCP tools report that no graph is
available. They never invent facts.

## Install

### From the public Git marketplace

```sh
/plugin marketplace add polatefekaya/archy
/plugin install archy@archy
```

### From a local checkout

```sh
/plugin marketplace add /path/to/archy
/plugin install archy@archy
```

Restart the session after installation, then confirm the integration:

```sh
/plugin
/mcp
```

You should see `archy` installed and enabled, and an enabled `archy` MCP server running
`archy mcp stdio`.

## What the hooks do

| Event | Trigger | Behaviour |
| --- | --- | --- |
| `SessionStart` | `startup`, `resume`, `clear`, `compact` | Injects a bounded architecture and rule summary as additional context: graph revision, current deterministic findings, introduced findings. |
| `PostToolUse` | after `Bash`, `Edit`, `Write` | Re-checks the touched paths. An introduced deterministic finding stops the current turn with remediation text. Stays silent when clean. |
| `Stop` | end of turn | Finalizes session state and flushes summaries. |

These hooks are feedback **after** an operation. They cannot prevent or roll back a completed
write, and they do not replace local Git hooks or required CI. Use `archy verify` as the delivery
gate.

`PostToolUse` maps every file-mutating tool Claude Code exposes: `Bash`, `Edit`, `MultiEdit`,
`NotebookEdit`, and `Write`.

Hook commands invoke `archy agent-hook <event>`. The original `archy codex-hook <event>` spelling
still routes to the same handler, so a plugin pinned to either name keeps working.

## MCP tools

The server publishes 22 tools covering graph and rule queries, similarity and reuse guidance,
planning, readiness, decisions, and sessions. Descriptions and JSON input schemas are published
through MCP `tools/list`.

Start with `resolve_symbol` when you hold a plain name: the other graph tools are addressed by
stable identifier, and `resolve_symbol` returns real identifiers read from the graph instead of
requiring you to construct one.

Every tool answers with a short summary **and** the full payload, both in the `content` text block
and in `structuredContent`. A client that surfaces only `content` still receives the answer.

The hooks and advisory tools do not claim to prevent a file write. Only configured, deterministic,
confidence-`1.0` graph facts can fail `archy verify`.
