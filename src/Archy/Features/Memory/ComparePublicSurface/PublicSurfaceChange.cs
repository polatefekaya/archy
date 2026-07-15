namespace Archy.Features.Memory.ComparePublicSurface;

public sealed record PublicSurfaceChange(
    PublicSurfaceChangeKind Kind,
    string SymbolId,
    string? PriorNodeStableId,
    string? CurrentNodeStableId,
    string? PriorSignatureHash,
    string? CurrentSignatureHash,
    IReadOnlyList<string> PriorFingerprintHashes,
    IReadOnlyList<string> CurrentFingerprintHashes);
