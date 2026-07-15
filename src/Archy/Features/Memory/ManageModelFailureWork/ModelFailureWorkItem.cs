using Archy.Features.Memory.ModelProviders.Contracts;

namespace Archy.Features.Memory.ManageModelFailureWork;

public sealed record ModelFailureWorkItem(
    string WorkItemId,
    ModelFailureWorkKind Kind,
    string? SummaryBatchId,
    string TargetStableId,
    long SourceGraphRevision,
    ModelFailureWorkState State,
    ModelProviderFailureKind FailureKind,
    int AttemptCount,
    DateTimeOffset? NextAttemptAtUtc,
    string MetadataJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
