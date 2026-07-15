namespace Archy.Features.Duplicates.CompareInterfaceSignatures;

/// <summary>Normalized independently of a language parser so every adapter can contribute the same duplicate signal.</summary>
public sealed record InterfaceSignature(
    string SymbolId,
    string Name,
    IReadOnlyList<string> ParameterTypes,
    string? ReturnType,
    int GenericArity);
