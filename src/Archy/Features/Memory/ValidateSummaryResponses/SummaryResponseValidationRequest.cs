using Archy.Features.Memory.ModelProviders.Contracts;
using Archy.Features.Memory.SummaryBatches;

namespace Archy.Features.Memory.ValidateSummaryResponses;

public sealed record SummaryResponseValidationRequest(
    SummaryBatch Batch,
    string ExpectedTargetStableId,
    string ExpectedRequestId,
    string ExpectedModelId,
    StructuredSummaryResponse Response);
