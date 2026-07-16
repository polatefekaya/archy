namespace Archy.Features.Graph.ExploreGraph;

/// <summary>A bounded, connected graph view centred on a meaningful node rather than an arbitrary storage page.</summary>
public sealed record GraphExplorerRequest(string? FocusStableId, long? Revision, int MaximumNodes);
