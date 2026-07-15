using Archy.Features.Analysis.LanguageSemanticAdapters;

namespace Archy.Features.Analysis.AnalyzeLanguageServerSemantics;

/// <summary>One capability-bounded semantic relationship collection attempt.</summary>
internal sealed record SemanticRelationshipOperation<T>(
    SemanticCapability Capability,
    IReadOnlyList<T> Facts,
    IReadOnlyList<SemanticDiagnostic> Diagnostics);
