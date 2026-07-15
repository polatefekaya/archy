namespace Archy.Features.Memory.Summaries;

public sealed record SummaryVersionFact(
    string SummaryId,
    string TargetKind,
    string TargetStableId,
    long SourceGraphRevision,
    string? SourceRepositoryCommit,
    string SummaryText,
    string EnglishDiff,
    string Provider,
    string Model,
    string ProviderMetadataJson,
    SummaryStaleness Staleness,
    string? OriginatingSummaryBatchId = null);
