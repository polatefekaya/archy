---
name: architecture-preflight
description: Check architecture rules, dependents, reuse candidates, and placement before writing code in a repository that has an Archy workspace. Use before creating a new file or type, before changing a boundary or public signature, before deleting or moving code, and when deciding where a new capability belongs.
---

# Architecture preflight with Archy

This repository may have an Archy workspace: a versioned architecture graph with layer rules,
dependency edges, and reuse evidence. When it does, query it **before** writing rather than
discovering a violation afterwards.

## When to use which tool

| Situation | Tool |
| --- | --- |
| Holding a plain type, member, or file name rather than a stable ID | `resolve_symbol` |
| About to add a new capability and unsure where it belongs | `suggest_placement` |
| About to write something that may already exist | `find_similar`, then `check_duplicate` |
| About to change or delete a type, method, or module | `get_dependents` |
| About to cross a layer or module boundary | `get_module_rules`, then `check_violation` |
| Wondering whether a prior decision already settled this | `get_decisions` |
| Made a deliberate architectural choice worth persisting | `record_decision` |

Start with `start_session` when beginning a multi-step change, and `end_session` when finished, so
decisions and summaries are attributed.

## Reading results

Every tool answers with a short summary followed by the full payload, and returns the same payload
in `structuredContent`. The summary is a count; the payload below it is the answer. Read past the
count:

- `resolve_symbol` → `candidates[]` with `stableId`, `filePath`, `nodeKind`, `confidence`
- `get_dependents` → `edges[]` with `source`, `target`, `kind`, `confidence`
- `find_similar` → `candidates[]` with `filePath`, `score`, and `evidence[].reason`
- `check_violation` → introduced versus legacy finding counts
- `get_module_rules` → resolved `layer` and rule context

A payload too large to mirror is clipped with an explicit `[truncated after …]` marker and remains
complete in `structuredContent`. Absence of that marker means you are reading the whole answer.

## Stable IDs

Graph nodes are addressed by stable ID, not by bare type name. The C# form is:

```
csharp:type:<repo-relative-path>:<FullyQualified.TypeName>
```

Never construct one by hand. Call `resolve_symbol` with the plain name and use the `stableId` it
returns. When it abstains, the symbol is outside the configured scope or the graph predates it —
that is "unknown", not "does not exist".

## Limits — do not overstate these results

- Only configured, deterministic, confidence-`1.0` graph facts can fail `archy verify`. Summaries,
  duplicate findings, placement suggestions, and health scores are **advisory**.
- These tools cannot block or roll back a file write. `archy verify` in CI is the delivery gate.
- If no workspace is initialized, or a language server is unavailable, Archy reports degraded
  coverage. Treat that as "unknown", never as "no problems found".
- Advisory output is evidence, not permission. Report what the evidence says, including when it
  contradicts the change you were about to make.
