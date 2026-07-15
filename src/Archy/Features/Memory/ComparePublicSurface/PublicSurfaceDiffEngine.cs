using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;

namespace Archy.Features.Memory.ComparePublicSurface;

/// <summary>Compares normalized public declarations, not source bodies, so body-only edits do not trigger summary prompts.</summary>
public sealed class PublicSurfaceDiffEngine : IPublicSurfaceDiffEngine
{
    public PublicSurfaceDiff Compare(GraphRevisionSnapshot prior, GraphRevisionSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(current);
        if (prior.Revision >= current.Revision)
        {
            throw new ArgumentException("Public-surface comparison requires a strictly older prior revision.", nameof(prior));
        }

        var priorSurface = SurfaceBySymbol(prior);
        var currentSurface = SurfaceBySymbol(current);
        var changes = new List<PublicSurfaceChange>();
        foreach (var symbolId in priorSurface.Keys.Union(currentSurface.Keys, StringComparer.Ordinal).OrderBy(static id => id, StringComparer.Ordinal))
        {
            var hasPrior = priorSurface.TryGetValue(symbolId, out var priorSymbol);
            var hasCurrent = currentSurface.TryGetValue(symbolId, out var currentSymbol);
            if (!hasPrior)
            {
                changes.Add(ToChange(PublicSurfaceChangeKind.Added, symbolId, null, currentSymbol!));
                continue;
            }

            if (!hasCurrent)
            {
                changes.Add(ToChange(PublicSurfaceChangeKind.Removed, symbolId, priorSymbol!, null));
                continue;
            }

            if (!SurfaceEquals(priorSymbol!, currentSymbol!))
            {
                changes.Add(ToChange(PublicSurfaceChangeKind.Changed, symbolId, priorSymbol, currentSymbol));
            }
        }

        return new PublicSurfaceDiff(prior.Revision, current.Revision, changes);
    }

    private static Dictionary<string, PublicSurfaceSymbol> SurfaceBySymbol(GraphRevisionSnapshot snapshot)
    {
        var fingerprintHashes = snapshot.InterfaceFingerprints
            .GroupBy(static fingerprint => fingerprint.SymbolId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<string>)[.. group
                    .OrderBy(static fingerprint => fingerprint.FingerprintKind, StringComparer.Ordinal)
                    .Select(static fingerprint => $"{fingerprint.FingerprintKind}:{fingerprint.FingerprintHash}")],
                StringComparer.Ordinal);
        return snapshot.Symbols
            .Where(static symbol => string.Equals(symbol.Visibility, "public", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static symbol => symbol.SymbolId, StringComparer.Ordinal)
            .ToDictionary(
                static symbol => symbol.SymbolId,
                symbol => new PublicSurfaceSymbol(
                    symbol.NodeStableId,
                    symbol.SignatureHash,
                    fingerprintHashes.GetValueOrDefault(symbol.SymbolId, [])),
                StringComparer.Ordinal);
    }

    private static PublicSurfaceChange ToChange(
        PublicSurfaceChangeKind kind,
        string symbolId,
        PublicSurfaceSymbol? prior,
        PublicSurfaceSymbol? current) =>
        new(
            kind,
            symbolId,
            prior?.NodeStableId,
            current?.NodeStableId,
            prior?.SignatureHash,
            current?.SignatureHash,
            prior?.FingerprintHashes ?? [],
            current?.FingerprintHashes ?? []);

    private static bool SurfaceEquals(PublicSurfaceSymbol left, PublicSurfaceSymbol right) =>
        string.Equals(left.SignatureHash, right.SignatureHash, StringComparison.Ordinal) &&
        left.FingerprintHashes.SequenceEqual(right.FingerprintHashes, StringComparer.Ordinal);

    private sealed record PublicSurfaceSymbol(
        string NodeStableId,
        string SignatureHash,
        IReadOnlyList<string> FingerprintHashes);
}
