# External language-server process protocol

`lsp-process/v1` is the boundary between Archy's Native AOT host and a language server. It is independent from the semantic-fact contract (`language-semantic/v2`): the language server speaks standard LSP over stdio, and the adapter translates its results into the semantic contract after validating that they belong to the immutable analysis snapshot.

## Launch and ownership

The host starts only a configured executable plus a structured argument array; it never builds a shell command. The repository root is the server working directory and `rootUri`/workspace folder. Standard output is reserved exclusively for LSP. Standard error is captured as bounded diagnostic text and is never parsed as protocol data. The launch specification bounds stdout message size, stderr capture, initialize/request/shutdown timeouts, restart count, and exponential restart backoff.

The server receives an `initialize` JSON-RPC request with ID `1`, JSON-RPC version `2.0`, a `file:` root URI, client capabilities for document symbols, definitions, references, call hierarchy, and type definitions, and Archy's non-breaking initialization options. These options declare `lsp-process/v1`, `language-semantic/v2`, and that returned facts must be attributable to the current snapshot. LSP servers may ignore unknown initialization options under the LSP specification.

After a successful response to request ID `1`, the host requires an object `result.capabilities`, parses the server's optional `serverInfo.name` and `serverInfo.version` as a version report, and sends the `initialized` notification. An absent version report is explicitly represented as unknown, never fabricated. Standard `documentSymbolProvider`, `definitionProvider`, `referencesProvider`, `callHierarchyProvider`, and `typeDefinitionProvider` advertisements are normalized into available, unavailable, or degraded status. A missing capability degrades only its dependent semantic facts.

## Framing and failure behavior

Messages use UTF-8 bytes framed by an ASCII `Content-Length` header and `\r\n\r\n` header terminator. Header names are case-insensitive when read; exactly one non-negative `Content-Length` is required. The Native AOT transport rejects oversize messages, duplicate/conflicting length headers, malformed JSON, and malformed responses; it routes concurrent numeric response IDs, records unmatched responses and server notifications, responds method-not-found to unsupported server requests, and captures bounded stderr separately from protocol stdout.

An initialize timeout, request timeout, malformed response, or process exit ends the current session without producing semantic facts. The host captures bounded stderr, terminates that process, and may restart up to the configured budget. Each restarted session begins with a fresh process, explicit restart event, and `initialize` request ID `1`. Once the budget is exhausted, the semantic adapter reports degraded coverage; it neither invents targets nor activates a partial graph revision.

`LanguageServerStdioSession` implements the host side of this contract. It sends `$/cancelRequest` when a pending request is cancelled and discards that request's later response without corrupting subsequent routing. A mock transcript covers initialization, server notifications, concurrent requests, cancellation, shutdown, and an `osx-arm64` Native AOT client fixture.

## Profile-driven document synchronization

`LanguageServerDocumentSynchronizer` is profile-driven. It accepts only hash-verified UTF-8 text buffers and emits standard `textDocument/didOpen` and `didClose` notifications using the profile's language ID. Each immutable analysis session opens a complete snapshot at version `1`, requests facts, closes every document deterministically, then shuts down.

A notification failure invalidates that analysis attempt because Archy cannot prove what the server observed; it emits no semantic facts. Immutable sessions deliberately do not emit `didChange`; incremental synchronization is deferred until Archy adds a separately verified session-reuse path.

## Generic relationship collection

After document symbols succeed, the profile-driven engine collects `textDocument/references` for every normalized symbol when `referencesProvider` is available. It collects outgoing calls for normalized method symbols with `textDocument/prepareCallHierarchy` followed by `callHierarchy/outgoingCalls` when `callHierarchyProvider` is available. Each capability is independently bounded by the profile's `max_symbol_queries`; crossing the bound, a malformed response, or a request failure degrades that capability and emits none of its partial relationship facts.

`DocumentSymbol.selectionRange` remains the stable declaration range used for canonical identity and query positions. Its enclosing `DocumentSymbol.range` is preserved as the optional semantic scope range and is used only to attribute reference locations to the smallest containing symbol. A call hierarchy target is resolved by its `selectionRange`; each outgoing call's `fromRanges` becomes immutable graph evidence in the caller document. This distinction prevents source edits inside a declaration body from changing its canonical identity while still making call and reference attribution sound.

Definitions, type definitions, inheritance, incoming calls, type facts, and semantic receiver resolution remain unavailable. They require a reliable language-neutral query-site model beyond declaration positions and are not inferred from a server capability advertisement.

## Compatibility

The protocol schema is `lsp-process/v1`. A breaking change requires a new schema version, compatibility test, and an explicit adapter selection rule. This contract does not define a generic Node/Python sidecar protocol; those separately versioned tools are deferred to T-130.
