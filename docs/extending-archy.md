# Extending Archy

Archy uses vertical slices: each request, domain model, persistence implementation, transport adapter, and tests stay in the feature folder that owns the behavior. Cross-slice contracts belong in `SharedKernel` only when they express a durable domain concept.

## Add a language/LSP profile

Language integration is configuration driven. Add a `language_server_profiles` entry with a stable ID, file extensions, activation markers, executable and arguments, LSP language ID, canonical identity prefix, symbol-kind mapping, and bounded symbol-query limits. Do not add a language-specific switch to the host. Exercise the profile selector and stdio protocol tests with both a complete fixture and each degraded precondition.

## Add a deterministic provider pattern

Place the C# pattern definition and semantic provider together under `Features/Analysis`. Emit a stable provider name, edge kind, confidence, source target, and JSON evidence. Only facts at confidence `1.0` may participate in configured hard enforcement. Add unit tests for matching and non-matching syntax and an integration test that commits a revision and validates the fact is readable through the graph API.

## Sidecar contract

Sidecars exchange bounded, versioned messages through `Features/Sidecars/Protocol`. Validate the message before translating it into domain facts. Pin sidecar package lock files, include them in release checksums, and report unavailable/tampered sidecars as degraded advisory coverage. Never allow a sidecar result to bypass graph identity, evidence, or confidence validation.

## AOT and test gates

Production code must remain trim-safe and Native AOT compatible: avoid runtime reflection serialization and ensure transport JSON has explicit writers. Run `dotnet build Archy.sln --no-restore`, the full test suite, the frontend build, and an AOT publish before release. For a new feature, include unit tests for pure logic, integration tests for SQLite/transport contracts, and a failure-path test that proves it preserves the last usable graph.
