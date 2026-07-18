namespace Archy.Features.Queries.FindSimilar;

public sealed record SimilarCodeQuery(string Query, string? SourceStableId, int Limit);
