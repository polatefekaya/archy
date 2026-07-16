using Archy.Features.Graph.TraverseDependencies;

namespace Archy.Features.Queries.MapNaturalLanguageQuery;

public sealed record ArchitectureQueryIntent(string StableId, GraphTraversalDirection Direction, int MaxDepth, long? Revision);
