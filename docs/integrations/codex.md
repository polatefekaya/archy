# Codex integration

Archy's Codex package lives at [`plugins/archy`](../../plugins/archy). It starts the local stdio MCP server and adds lifecycle hooks. Install only after the Native AOT `archy` executable is available on `PATH`; the package never carries secrets.

The MCP server is per repository. Its tool set includes dependency/rule/duplicate/placement queries, hybrid similarity and reuse explanations, change planning, architecture explanations, the read-only `get_doctor_readiness` report, and advisory `preflight_change`, as well as decision/session control. `preflight_change` gives bounded context before an edit but never blocks a write or observes future filesystem changes. The optional HTTP transport binds only to loopback and requires an explicit bearer token; stdio is the default.

## MCP tool contract

Every tool publishes a non-empty description and a JSON input schema through MCP `tools/list`. Invalid required arguments return an MCP tool error. Missing graph, history, embedding, or cluster evidence is reported as an explicit abstention when the operation can still answer safely.

| Tool | Agent-facing behavior | State/prerequisite |
| --- | --- | --- |
| `check_violation` | Refresh and evaluate deterministic architecture findings | May update local graph state; never edits the worktree |
| `get_module_rules` | Resolve one graph node's layer and rule context | Requires a known active graph target |
| `get_dependents` | Read bounded direct/transitive dependents | Requires a committed graph revision |
| `impact_analysis` | Read bounded dependency and public-surface impact | Advisory; requires a known graph target |
| `check_duplicate` | Read persisted duplicate evidence | Abstains when no evidence exists |
| `find_similar` | Rank explainable structural/optional embedding candidates | May cache a consented query vector; never edits source |
| `why_not_reuse` | Explain reuse, extension, or separation for one candidate | Advisory; requires persisted structural evidence |
| `get_similarity_cluster` | Read a current or historical immutable cluster | Abstains when the requested cluster/revision is unavailable |
| `find_reintroduced` | Compare an active declaration with removed historical capabilities | Abstains when sufficient history is unavailable |
| `suggest_placement` | Score declared dependencies against persisted placement clusters | Advisory; abstains without cluster evidence |
| `plan_change` | Produce a bounded change/reuse/validation plan | Advisory; does not edit files |
| `preflight_change` | Orchestrate bounded pre-edit context and checks | Advisory; optional session metadata excludes source/snippets |
| `safe_refactor` | Produce phased refactor checkpoints | Advisory; does not perform the refactor |
| `explain_architecture` | Explain facts, decisions, history, and unknowns for a target | Separates deterministic facts from advisory context |
| `compose_change_summary` | Produce a Markdown-ready graph delta | Requires an explicit base graph revision; never posts remotely |
| `get_doctor_readiness` | Read repository, graph, language, hook, and embedding readiness | Non-mutating; never initializes or analyzes |
| `record_decision` | Persist a decision against architecture targets | Writes local Archy decision/session state only |
| `get_decisions` | Read decisions for one architecture target | Read-only local-state query |
| `start_session` | Start an attributed architecture session | Writes local Archy session state |
| `flush_summaries` | Queue durable deferred summary work | Does not call a model on the request path |
| `end_session` | End a session and defer summary processing | Writes local Archy session state |

The released-host integration suite starts the real stdio server, verifies the exact 21-name catalog, parses every published schema, rejects empty arguments for every tool that requires input, and successfully invokes all 21 tools in dependency-safe order.

After installing or upgrading Archy, start a new Codex task so the app launches the new binary and reloads its MCP catalog. An already-running MCP process does not hot-reload a replaced executable.

`get_doctor_readiness` is the MCP equivalent of `archy doctor`: it never initializes a workspace, migrates state, starts a language server, runs analysis, makes a model request, or exposes credentials/vectors. It reports bounded readiness checks and remediation only.

`compose_change_summary` compares an explicit base graph revision with the active revision and returns deterministic, Markdown-ready node/edge delta evidence. It abstains when the base revision is unavailable rather than treating Git HEAD as graph provenance, and it never creates a commit, PR, or remote-provider request.

Codex hooks are guidance around a completed operation. `SessionStart` adds a bounded persisted graph/rule summary (revision, representative paths, and public-surface samples) without inspecting a prompt or calling a model. `PostToolUse` covers `Bash|Edit|Write`, then can return a turn stop for an introduced deterministic verification finding. It is not a pre-write guard and cannot protect arbitrary local file writes.

After a completed edit without an introduced deterministic finding, `PostToolUse` can offer bounded structural reuse guidance from the persisted graph. That graph may predate the edit; the hook labels this explicitly and recommends `archy analyze` when changed paths have no persisted graph nodes. It never sends changed source text to a model and never fails closed because reuse evidence is unavailable.

Use this flow:

1. Query Archy before a boundary-sensitive edit.
2. Address any post-edit turn stop and run `archy verify`.
3. Deliver through the installed Git gate and required `Archy / verify` CI check.

See [enforcement semantics](../architecture/enforcement.md) for bypass limits and the required branch-protection setup.
