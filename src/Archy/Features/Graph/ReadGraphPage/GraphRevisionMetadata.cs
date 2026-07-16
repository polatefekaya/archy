namespace Archy.Features.Graph.ReadGraphPage;

/// <summary>Small, deterministic metadata for a committed graph revision.</summary>
public sealed record GraphRevisionMetadata(long Revision, int NodeCount, int EdgeCount);
