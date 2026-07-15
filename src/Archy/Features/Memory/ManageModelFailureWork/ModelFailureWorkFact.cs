using Archy.Features.Memory.ModelProviders.Contracts;

namespace Archy.Features.Memory.ManageModelFailureWork;

public sealed record ModelFailureWorkFact(
    string WorkItemId,
    ModelFailureWorkKind Kind,
    string? SummaryBatchId,
    string TargetStableId,
    long SourceGraphRevision,
    ModelProviderFailure Failure,
    int AttemptCount,
    DateTimeOffset? NextAttemptAtUtc,
    string MetadataJson);
