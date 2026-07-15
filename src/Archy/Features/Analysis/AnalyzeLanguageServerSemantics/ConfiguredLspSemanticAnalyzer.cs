using System.Text.Json;
using Archy.Features.Analysis.ExternalLanguageServerProtocol;
using Archy.Features.Analysis.ExternalLanguageServerProtocol.EstablishStdioSession;
using Archy.Features.Analysis.ExternalLanguageServerProtocol.NormalizeSemanticResponses;
using Archy.Features.Analysis.LanguageSemanticAdapters;
using Archy.Features.Analysis.LanguageServerProfiles;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.AnalyzeLanguageServerSemantics;

/// <summary>Generic, profile-driven standard-LSP declaration-symbol analysis.</summary>
public sealed class ConfiguredLspSemanticAnalyzer(
    ILanguageServerProfileSelector profileSelector,
    ILanguageServerSessionFactory sessionFactory) : IConfiguredLspSemanticAnalyzer
{
    private static readonly string[] ContractCapabilities = ["documentSymbol", "definition", "references", "callHierarchy", "typeDefinition"];

    public async ValueTask<Result<SemanticAnalysisResult>> AnalyzeAsync(ConfiguredLspSemanticAnalysisRequest request, CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (validation is not null)
        {
            return ResultFactory.Failure<SemanticAnalysisResult>(validation);
        }

        var selection = profileSelector.Resolve(request.SemanticRequest.RepositoryRoot, request.Profile, request.SemanticRequest.Documents.Count > 0);
        if (selection.State != LanguageServerProfileSelectionState.Selected)
        {
            return CreateDegraded(
                request,
                selection.State == LanguageServerProfileSelectionState.NotApplicable ? SemanticCapabilityState.Unavailable : SemanticCapabilityState.Degraded,
                selection.Diagnostics.Select(static diagnostic => new SemanticDiagnostic(diagnostic.Code, "warning", diagnostic.Message, null)));
        }

        ConfiguredLspSemanticSymbolIdentityPolicy identityPolicy;
        try
        {
            identityPolicy = new ConfiguredLspSemanticSymbolIdentityPolicy(request.Profile);
        }
        catch (ArgumentException exception)
        {
            return ResultFactory.Failure<SemanticAnalysisResult>(Problem.Validation(exception.Message));
        }

        var buffers = await SnapshotDocumentLoader.LoadAsync(request.SemanticRequest, cancellationToken);
        if (!buffers.IsSuccess)
        {
            return ResultFactory.Failure<SemanticAnalysisResult>(buffers.Problem!);
        }

        var specification = CreateLaunchSpecification(request.SemanticRequest.RepositoryRoot, request.Profile, selection.ExecutablePath!);
        var started = await sessionFactory.StartAsync(specification, Environment.ProcessId, cancellationToken);
        if (!started.IsSuccess)
        {
            return CreateDegraded(request, SemanticCapabilityState.Degraded, [new SemanticDiagnostic("language_server_session_unavailable", "warning", started.Problem!.Message, null)]);
        }

        await using var session = started.Value;
        try
        {
            var documentCapability = Capability(session.CapabilityProfile, "documentSymbol");
            if (documentCapability.State != LanguageServerCapabilityState.Available)
            {
                return CreateDegraded(request, ToSemanticState(documentCapability.State), [new SemanticDiagnostic("language_server_document_symbols_unavailable", "warning", documentCapability.Detail ?? "The language server did not advertise document-symbol support.", null)], session.CapabilityProfile);
            }

            var opened = await LanguageServerDocumentSynchronizer.OpenAsync(session, request.SemanticRequest.RepositoryRoot, request.Profile.LanguageId, buffers.Value, cancellationToken);
            if (opened is not null)
            {
                return CreateDegraded(request, SemanticCapabilityState.Degraded, [new SemanticDiagnostic("language_server_document_synchronization_failed", "warning", opened.Message, null)], session.CapabilityProfile);
            }

            var symbols = new List<SemanticSymbol>();
            foreach (var document in buffers.Value)
            {
                var response = await RequestDocumentSymbolsAsync(session, request.SemanticRequest.RepositoryRoot, document.RepositoryRelativePath, specification.Timeouts.RequestMilliseconds, cancellationToken);
                if (!response.IsSuccess)
                {
                    return CreateDegraded(request, SemanticCapabilityState.Degraded, [new SemanticDiagnostic("language_server_document_symbols_failed", "warning", response.Problem!.Message, null)], session.CapabilityProfile);
                }

                using (response.Value)
                {
                    var payload = response.Value.RootElement.GetProperty("result");
                    if (payload.ValueKind == JsonValueKind.Null)
                    {
                        continue;
                    }

                    var normalized = LspSemanticResponseNormalizer.ExtractDocumentSymbols(document.RepositoryRelativePath, payload, identityPolicy);
                    if (!normalized.IsSuccess)
                    {
                        return CreateDegraded(request, SemanticCapabilityState.Degraded, [new SemanticDiagnostic("language_server_document_symbols_invalid", "warning", normalized.Problem!.Message, null)], session.CapabilityProfile);
                    }

                    symbols.AddRange(normalized.Value);
                }
            }

            if (symbols.Select(static symbol => symbol.CanonicalId).Distinct(StringComparer.Ordinal).Count() != symbols.Count)
            {
                return CreateDegraded(request, SemanticCapabilityState.Degraded, [new SemanticDiagnostic("language_server_semantic_symbol_identity_conflict", "warning", "The declarative LSP profile produced duplicate symbol identities across the snapshot.", null)], session.CapabilityProfile);
            }

            var orderedSymbols = symbols.OrderBy(static symbol => symbol.CanonicalId, StringComparer.Ordinal).ToArray();
            var references = await CollectReferencesAsync(session, request, orderedSymbols, specification.Timeouts.RequestMilliseconds, cancellationToken);
            var calls = await CollectOutgoingCallsAsync(session, request, orderedSymbols, specification.Timeouts.RequestMilliseconds, cancellationToken);
            var closed = await LanguageServerDocumentSynchronizer.CloseAsync(session, request.SemanticRequest.RepositoryRoot, buffers.Value, cancellationToken);
            if (closed is not null)
            {
                return CreateDegraded(request, SemanticCapabilityState.Degraded, [new SemanticDiagnostic("language_server_document_close_failed", "warning", closed.Message, null)], session.CapabilityProfile);
            }

            return CreateResult(
                request,
                CompletedCapabilities(session.CapabilityProfile, references.Capability, calls.Capability),
                orderedSymbols,
                [],
                references.Facts,
                calls.Facts,
                [],
                references.Diagnostics.Concat(calls.Diagnostics));
        }
        finally
        {
            _ = await session.ShutdownAsync(CancellationToken.None);
        }
    }

    private static ValueTask<Result<JsonDocument>> RequestDocumentSymbolsAsync(ILanguageServerSession session, string repositoryRoot, string repositoryRelativePath, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        var parameters = JsonSerializer.SerializeToElement(
            new LspDocumentSymbolParameters(new LspTextDocumentIdentifier(new Uri(Path.Combine(Path.GetFullPath(repositoryRoot), repositoryRelativePath)).AbsoluteUri)),
            ConfiguredLspSemanticRequestJsonContext.Default.LspDocumentSymbolParameters);
        return RequestArrayResultAsync(session, "textDocument/documentSymbol", parameters, timeoutMilliseconds, cancellationToken);
    }

    private static async ValueTask<Result<JsonDocument>> RequestArrayResultAsync(ILanguageServerSession session, string method, JsonElement parameters, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMilliseconds);
        var response = await session.SendRequestAsync(method, parameters, timeout.Token);
        if (!response.IsSuccess)
        {
            return ResultFactory.Failure<JsonDocument>(response.Problem!);
        }

        if (!response.Value.RootElement.TryGetProperty("result", out var result) || result.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null))
        {
            response.Value.Dispose();
            return ResultFactory.Failure<JsonDocument>(Problem.Validation($"Language-server '{method}' response did not contain an array or null result."));
        }

        return response;
    }

    private static async ValueTask<SemanticRelationshipOperation<SemanticReference>> CollectReferencesAsync(
        ILanguageServerSession session,
        ConfiguredLspSemanticAnalysisRequest request,
        SemanticSymbol[] symbols,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        const string capabilityName = "references";
        var advertised = Capability(session.CapabilityProfile, capabilityName);
        if (advertised.State != LanguageServerCapabilityState.Available)
        {
            return UnavailableOperation<SemanticReference>(capabilityName, advertised);
        }

        if (symbols.Length > request.Profile.MaxSymbolQueries)
        {
            return QueryLimitExceeded<SemanticReference>(capabilityName, "language_server_references_query_limit_exceeded", symbols.Length, request.Profile.MaxSymbolQueries);
        }

        var references = new List<SemanticReference>();
        foreach (var target in symbols)
        {
            var parameters = JsonSerializer.SerializeToElement(
                new LspReferenceParameters(
                    DocumentIdentifier(request.SemanticRequest.RepositoryRoot, target.Range.RepositoryRelativePath),
                    Position(target.Range),
                    new LspReferenceContext(IncludeDeclaration: false)),
                ConfiguredLspSemanticRequestJsonContext.Default.LspReferenceParameters);
            var response = await RequestArrayResultAsync(session, "textDocument/references", parameters, timeoutMilliseconds, cancellationToken);
            if (!response.IsSuccess)
            {
                return QueryFailed<SemanticReference>(capabilityName, "language_server_references_failed", response.Problem!.Message);
            }

            using (response.Value)
            {
                var normalized = LspSemanticRelationshipNormalizer.ExtractReferences(target.CanonicalId, request.SemanticRequest.RepositoryRoot, response.Value.RootElement.GetProperty("result"), symbols);
                if (!normalized.IsSuccess)
                {
                    return QueryFailed<SemanticReference>(capabilityName, "language_server_references_invalid", normalized.Problem!.Message);
                }

                references.AddRange(normalized.Value);
            }
        }

        return CompletedOperation(capabilityName, advertised.Detail, references
            .Distinct()
            .OrderBy(static reference => reference.SourceCanonicalId, StringComparer.Ordinal)
            .ThenBy(static reference => reference.TargetCanonicalId, StringComparer.Ordinal)
            .ThenBy(static reference => reference.Range.RepositoryRelativePath, StringComparer.Ordinal)
            .ThenBy(static reference => reference.Range.StartLine)
            .ThenBy(static reference => reference.Range.StartColumn));
    }

    private static async ValueTask<SemanticRelationshipOperation<SemanticCall>> CollectOutgoingCallsAsync(
        ILanguageServerSession session,
        ConfiguredLspSemanticAnalysisRequest request,
        SemanticSymbol[] symbols,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        const string capabilityName = "callHierarchy";
        var advertised = Capability(session.CapabilityProfile, capabilityName);
        if (advertised.State != LanguageServerCapabilityState.Available)
        {
            return UnavailableOperation<SemanticCall>(capabilityName, advertised);
        }

        var callers = symbols.Where(static symbol => symbol.Kind == SemanticSymbolKind.Method).ToArray();
        if (callers.Length > request.Profile.MaxSymbolQueries)
        {
            return QueryLimitExceeded<SemanticCall>(capabilityName, "language_server_call_hierarchy_query_limit_exceeded", callers.Length, request.Profile.MaxSymbolQueries);
        }

        var calls = new List<SemanticCall>();
        foreach (var caller in callers)
        {
            var prepareParameters = JsonSerializer.SerializeToElement(
                new LspTextDocumentPositionParameters(
                    DocumentIdentifier(request.SemanticRequest.RepositoryRoot, caller.Range.RepositoryRelativePath),
                    Position(caller.Range)),
                ConfiguredLspSemanticRequestJsonContext.Default.LspTextDocumentPositionParameters);
            var prepared = await RequestArrayResultAsync(session, "textDocument/prepareCallHierarchy", prepareParameters, timeoutMilliseconds, cancellationToken);
            if (!prepared.IsSuccess)
            {
                return QueryFailed<SemanticCall>(capabilityName, "language_server_call_hierarchy_prepare_failed", prepared.Problem!.Message);
            }

            using (prepared.Value)
            {
                var items = prepared.Value.RootElement.GetProperty("result");
                if (items.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                foreach (var item in items.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        return QueryFailed<SemanticCall>(capabilityName, "language_server_call_hierarchy_prepare_invalid", "Language-server call-hierarchy preparation returned a non-object item.");
                    }

                    var outgoingParameters = JsonSerializer.SerializeToElement(
                        new LspCallHierarchyOutgoingCallsParameters(item.Clone()),
                        ConfiguredLspSemanticRequestJsonContext.Default.LspCallHierarchyOutgoingCallsParameters);
                    var outgoing = await RequestArrayResultAsync(session, "callHierarchy/outgoingCalls", outgoingParameters, timeoutMilliseconds, cancellationToken);
                    if (!outgoing.IsSuccess)
                    {
                        return QueryFailed<SemanticCall>(capabilityName, "language_server_call_hierarchy_outgoing_failed", outgoing.Problem!.Message);
                    }

                    using (outgoing.Value)
                    {
                        var normalized = LspSemanticRelationshipNormalizer.ExtractOutgoingCalls(caller.CanonicalId, request.SemanticRequest.RepositoryRoot, outgoing.Value.RootElement.GetProperty("result"), symbols);
                        if (!normalized.IsSuccess)
                        {
                            return QueryFailed<SemanticCall>(capabilityName, "language_server_call_hierarchy_outgoing_invalid", normalized.Problem!.Message);
                        }

                        calls.AddRange(normalized.Value);
                    }
                }
            }
        }

        return CompletedOperation(capabilityName, advertised.Detail, calls
            .Distinct()
            .OrderBy(static call => call.CallerCanonicalId, StringComparer.Ordinal)
            .ThenBy(static call => call.CalleeCanonicalId, StringComparer.Ordinal)
            .ThenBy(static call => call.Range.RepositoryRelativePath, StringComparer.Ordinal)
            .ThenBy(static call => call.Range.StartLine)
            .ThenBy(static call => call.Range.StartColumn));
    }

    private static SemanticRelationshipOperation<T> CompletedOperation<T>(string capabilityName, string? detail, IEnumerable<T> facts) =>
        new(new SemanticCapability(capabilityName, SemanticCapabilityState.Available, detail), [.. facts], []);

    private static SemanticRelationshipOperation<T> UnavailableOperation<T>(string capabilityName, LanguageServerCapabilityStatus advertised) =>
        new(new SemanticCapability(capabilityName, ToSemanticState(advertised.State), advertised.Detail ?? "The server did not advertise this capability."), [], []);

    private static SemanticRelationshipOperation<T> QueryLimitExceeded<T>(string capabilityName, string diagnosticCode, int queryCount, int maximumQueries)
    {
        var message = $"The configured profile limits {capabilityName} collection to {maximumQueries} symbol queries, but this snapshot requires {queryCount}.";
        return new(
            new SemanticCapability(capabilityName, SemanticCapabilityState.Degraded, message),
            [],
            [new SemanticDiagnostic(diagnosticCode, "warning", message, null)]);
    }

    private static SemanticRelationshipOperation<T> QueryFailed<T>(string capabilityName, string diagnosticCode, string message) =>
        new(
            new SemanticCapability(capabilityName, SemanticCapabilityState.Degraded, message),
            [],
            [new SemanticDiagnostic(diagnosticCode, "warning", message, null)]);

    private static LspTextDocumentIdentifier DocumentIdentifier(string repositoryRoot, string repositoryRelativePath) =>
        new(new Uri(Path.Combine(Path.GetFullPath(repositoryRoot), repositoryRelativePath)).AbsoluteUri);

    private static LspPosition Position(SemanticSourceRange range) => new(range.StartLine - 1, range.StartColumn - 1);

    private static LanguageServerLaunchSpecification CreateLaunchSpecification(string repositoryRoot, LanguageServerProfileConfiguration profile, string executablePath) => new(
        LanguageServerProtocolContract.CurrentSchemaVersion,
        profile.Id,
        profile.LanguageId,
        executablePath,
        profile.Arguments,
        repositoryRoot,
        new LanguageServerTimeouts(15_000, 30_000, 5_000),
        new LanguageServerRestartPolicy(0, 0),
        8 * 1024 * 1024,
        256 * 1024,
        [
            new LanguageServerCapabilityRequirement("documentSymbol", true),
            new LanguageServerCapabilityRequirement("definition", false),
            new LanguageServerCapabilityRequirement("references", false),
            new LanguageServerCapabilityRequirement("callHierarchy", false),
            new LanguageServerCapabilityRequirement("typeDefinition", false),
        ]);

    private static Result<SemanticAnalysisResult> CreateDegraded(ConfiguredLspSemanticAnalysisRequest request, SemanticCapabilityState state, IEnumerable<SemanticDiagnostic> diagnostics, LanguageServerCapabilityProfile? profile = null) =>
        CreateResult(request, DegradedCapabilities(state, profile), [], [], [], [], [], diagnostics);

    private static Result<SemanticAnalysisResult> CreateResult(
        ConfiguredLspSemanticAnalysisRequest request,
        IReadOnlyList<SemanticCapability> capabilities,
        IReadOnlyList<SemanticSymbol> symbols,
        IReadOnlyList<SemanticDefinition> definitions,
        IReadOnlyList<SemanticReference> references,
        IReadOnlyList<SemanticCall> calls,
        IReadOnlyList<SemanticInheritance> inheritance,
        IEnumerable<SemanticDiagnostic> diagnostics)
    {
        var result = new SemanticAnalysisResult(
            SemanticAnalysisContract.CurrentSchemaVersion,
            request.Profile.Id,
            request.Profile.LanguageId,
            request.SemanticRequest.SnapshotId,
            capabilities,
            symbols,
            definitions, references, calls, inheritance, [], [],
            [.. diagnostics.OrderBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)]);
        var validation = SemanticAnalysisContract.ValidateResult(result);
        return validation is null ? ResultFactory.Success(result) : ResultFactory.Failure<SemanticAnalysisResult>(validation);
    }

    private static IReadOnlyList<SemanticCapability> CompletedCapabilities(
        LanguageServerCapabilityProfile profile,
        SemanticCapability references,
        SemanticCapability calls) =>
        [
            new SemanticCapability("documentSymbol", SemanticCapabilityState.Available, Capability(profile, "documentSymbol").Detail),
            DeferredCapability("definition", profile),
            references,
            calls,
            DeferredCapability("typeDefinition", profile),
        ];

    private static IReadOnlyList<SemanticCapability> DegradedCapabilities(SemanticCapabilityState state, LanguageServerCapabilityProfile? profile) =>
        [new SemanticCapability("documentSymbol", state, profile is null ? "Language-server document-symbol coverage is unavailable." : Capability(profile, "documentSymbol").Detail ?? "Language-server document-symbol coverage is unavailable."), .. DeferredCapabilities(profile)];

    private static IEnumerable<SemanticCapability> DeferredCapabilities(LanguageServerCapabilityProfile? profile) =>
        ContractCapabilities
            .Where(static capability => capability is not ("documentSymbol" or "references" or "callHierarchy"))
            .Select(capability => DeferredCapability(capability, profile));

    private static SemanticCapability DeferredCapability(string capabilityName, LanguageServerCapabilityProfile? profile)
    {
        var advertised = profile is null
            ? new LanguageServerCapabilityStatus(capabilityName, LanguageServerCapabilityState.Unavailable, "The language-server session was not established.")
            : Capability(profile, capabilityName);
        return new SemanticCapability(
            capabilityName,
            SemanticCapabilityState.Unavailable,
            advertised.State == LanguageServerCapabilityState.Available
                ? "The server advertised this capability, but Archy has not implemented its query orchestration yet."
                : advertised.Detail ?? "The server did not advertise this capability.");
    }

    private static LanguageServerCapabilityStatus Capability(LanguageServerCapabilityProfile profile, string capabilityName) =>
        profile.Capabilities.SingleOrDefault(capability => string.Equals(capability.Name, capabilityName, StringComparison.Ordinal))
            ?? new LanguageServerCapabilityStatus(capabilityName, LanguageServerCapabilityState.Unavailable, "The language-server capability profile did not report this capability.");

    private static SemanticCapabilityState ToSemanticState(LanguageServerCapabilityState state) => state == LanguageServerCapabilityState.Degraded ? SemanticCapabilityState.Degraded : SemanticCapabilityState.Unavailable;

    private static Problem? Validate(ConfiguredLspSemanticAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var semanticValidation = SemanticAnalysisContract.ValidateRequest(request.SemanticRequest);
        if (semanticValidation is not null || request.Profile is null || string.IsNullOrWhiteSpace(request.ConfigurationFingerprint) || request.SemanticRequest.Documents.Any(document => !string.Equals(document.LanguageId, request.Profile.LanguageId, StringComparison.Ordinal) || !request.Profile.Extensions.Any(extension => document.RepositoryRelativePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))))
        {
            return semanticValidation ?? Problem.Validation("Configured LSP analysis requires a profile, configuration fingerprint, and documents matching the profile language and extensions.");
        }

        return null;
    }
}
