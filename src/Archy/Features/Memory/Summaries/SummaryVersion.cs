namespace Archy.Features.Memory.Summaries;

public sealed record SummaryVersion(
    string SummaryVersionId,
    string SummaryId,
    int VersionNumber,
    long SourceGraphRevision,
    string? SourceRepositoryCommit,
    string SummaryText,
    string EnglishDiff,
    string Provider,
    string Model,
    string ProviderMetadataJson,
    SummaryStaleness Staleness,
    string? SupersedesSummaryVersionId,
    DateTimeOffset CreatedAtUtc,
    string? OriginatingSummaryBatchId = null);
