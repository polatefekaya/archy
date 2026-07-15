using System.Text.Json;
using System.Text.Json.Serialization;

namespace Archy.Features.Analysis.AnalyzeLanguageServerSemantics;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LspDocumentSymbolParameters))]
[JsonSerializable(typeof(LspReferenceParameters))]
[JsonSerializable(typeof(LspTextDocumentPositionParameters))]
[JsonSerializable(typeof(LspCallHierarchyOutgoingCallsParameters))]
internal sealed partial class ConfiguredLspSemanticRequestJsonContext : JsonSerializerContext;

internal sealed record LspDocumentSymbolParameters(LspTextDocumentIdentifier TextDocument);

internal sealed record LspTextDocumentIdentifier(string Uri);

internal sealed record LspTextDocumentPositionParameters(
    LspTextDocumentIdentifier TextDocument,
    LspPosition Position);

internal sealed record LspPosition(int Line, int Character);

internal sealed record LspReferenceContext(bool IncludeDeclaration);

internal sealed record LspReferenceParameters(
    LspTextDocumentIdentifier TextDocument,
    LspPosition Position,
    LspReferenceContext Context);

internal sealed record LspCallHierarchyOutgoingCallsParameters(JsonElement Item);
