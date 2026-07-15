# Deterministic architecture enforcement

`archy verify` is the strict deterministic layer gate. It first runs the ordinary workspace analysis path, then reads the active immutable graph revision under a shared workspace lock. The result is evaluated against the same effective `archy.toml` used for analysis, so enforcement never combines facts from one revision with rules from another configuration load.

For every graph node with a repository source path, layer membership is one of: assigned, unassigned, or ambiguous. Nodes without a source path—configuration keys, message queues, and other virtual resources—are explicitly not applicable. When a repository declares layers, unassigned and ambiguous code nodes are coverage failures rather than silently ignored evidence.

An edge is eligible to block only if both conditions hold:

- Its kind is listed in `enforcement.hard_edge_kinds`.
- Its graph confidence is exactly `1.0`.

The second condition is deliberately non-configurable. Explicitly configured kinds cannot promote a 0.8 DI inference, a 0.65 string/pattern match, or any other advisory evidence into a hard violation. For eligible edges, a source layer may target itself or a layer named in its `may_depend_on` list. Any other cross-layer direction is a violation retaining the exact graph edge ID, kind, provider, confidence, and evidence.

The same eligible-edge policy drives cycle detection. Archy computes strongly connected components only over eligible edges whose endpoints have deterministic layer assignments, then reports one shortest deterministic evidence path per component. A cycle fails verification even when all of its individual layer directions are otherwise allowed.

`archy verify` compares those findings with the committed `archy.baseline.json` artifact at the repository root. With no accepted baseline, every current deterministic finding is introduced and fails verification. `archy baseline accept` is an explicit repository-changing action that writes the complete current finding set and its architecture-rule fingerprint; commit that file so local verification and CI evaluate the same debt. A compatible baseline classifies findings as `introduced`, `legacy`, or `resolved`; only introduced findings return exit code `1`. A baseline whose rule fingerprint no longer matches cannot suppress any finding and is reported as incompatible until it is deliberately accepted again.

An intentional temporary deviation is a separate, explicit decision in committed `archy.exceptions.json`. `archy exception accept` accepts exactly one current `introduced` finding key and requires an author, rationale, review timestamp, and expiry timestamp. Exception decisions are append-only, are selected only by exact finding-key equality, and can never match a pattern or suppress a whole layer, edge kind, directory, or rule family. An active selected decision changes only that finding to `excepted`; a passed review date is surfaced as `review_due`, while an expired decision remains `introduced` and therefore blocks verification. JSON verification output contains every active, review-due, expired, or unused exception decision with its current state, so graph explanation and future health projections can account for the exception rather than losing its provenance.

The command exits `0` when it has no introduced deterministic finding, `1` for introduced coverage, direction, or cycle findings, `2` for analysis/configuration/storage/policy errors, and `64` for invalid options. JSON output is available with `--json`.

`archy verify --sarif` writes a SARIF 2.1.0 report to standard output; use `--output <path>` to write the same report to a file. `--json` and `--sarif` are deliberately mutually exclusive. SARIF results are generated from the completed verification result—not by a second rule evaluator—and retain the same introduced, legacy, resolved, or excepted status in both SARIF baseline state and result properties. Rules are stable: `ARCHY001` for layer coverage, `ARCHY002` for an invalid hard dependency direction, and `ARCHY003` for a hard dependency cycle. When a finding has a graph-node source target, its repository-relative URI and line range are included for editor navigation; a failure to run verification is represented as an unsuccessful SARIF invocation. MCP presentation remains a separate slice.

## Local Git delivery gate

On macOS and other POSIX hosts, install the local gate with `archy hooks install`. It installs managed `pre-commit` and `pre-push` wrappers in Git's resolved hook directory, including a repository's configured `core.hooksPath`; inspect ownership with `archy hooks status`, and remove it with `archy hooks uninstall`.

Installation is reversible and idempotent. If either hook already exists, Archy moves it to the adjacent `*.archy-legacy` path and executes it first with its original arguments. The pre-push wrapper also preserves the original hook's standard input. Uninstall removes only a wrapper bearing Archy's versioned header and restores the preserved file byte-for-byte; it refuses to delete an unowned hook or an orphaned preservation file.

The wrappers never verify the developer's mutable checkout. Pre-commit writes the staged Git tree, and pre-push writes each distinct local tree that differs from its remote target, to a temporary Git repository. Each temporary tree gets isolated Archy state, is initialized, and then runs the canonical `archy verify`. This prevents unstaged files and persistent state from changing the enforcement result. As with all client-side Git hooks, `--no-verify` or local hook configuration can bypass the local gate; required CI remains the authoritative merge boundary.
