namespace Archy.Features.Memory.ComparePublicSurface;

public sealed record PublicSurfaceDiff(
    long PriorRevision,
    long CurrentRevision,
    IReadOnlyList<PublicSurfaceChange> Changes);
