using Archy.Features.Memory.ModelProviders.Contracts;
using Archy.Features.Memory.SummaryBatches;
using Archy.Features.Memory.ValidateSummaryResponses;

namespace Archy.UnitTests.Features.Memory.ValidateSummaryResponses;

public sealed class StructuredSummaryResponseValidatorTests
{
    [Fact]
    public void ValidateRejectsMismatchedAndMalformedModelOutputWithoutContent()
    {
        var batch = new SummaryBatch("batch", "session", "idle", SummaryBatchRequestState.Pending, 1, "{}", DateTimeOffset.UtcNow, [new SummaryBatchMember(1, "public_type", "type:a", 1, [])]);
        var response = new StructuredSummaryResponse("request", "model", "{\"targetStableId\":\"type:other\",\"summary\":\"A sufficiently detailed summary with real content.\",\"englishDiff\":\"Changed.\"}", new ModelUsage(1, 1, 0, 2), new ModelResponseMetadata("provider", "{}"));

        var result = new StructuredSummaryResponseValidator().Validate(new SummaryResponseValidationRequest(batch, "type:a", "request", "model", response));

        Assert.Equal(SummaryResponseValidationDisposition.RetryableFailure, result.Disposition);
        Assert.Null(result.Content);
    }

    [Fact]
    public void ValidateAcceptsTheExactSchemaAndExpectedTarget()
    {
        var batch = new SummaryBatch("batch", "session", "idle", SummaryBatchRequestState.Pending, 1, "{}", DateTimeOffset.UtcNow, [new SummaryBatchMember(1, "public_type", "type:a", 1, [])]);
        var response = new StructuredSummaryResponse("request", "model", "{\"targetStableId\":\"type:a\",\"summary\":\"This type coordinates scheduled clock updates and exposes a stable API.\",\"englishDiff\":\"Adds a stable scheduling boundary.\"}", new ModelUsage(1, 1, 0, 2), new ModelResponseMetadata("provider", "{}"));

        var result = new StructuredSummaryResponseValidator().Validate(new SummaryResponseValidationRequest(batch, "type:a", "request", "model", response));

        Assert.True(result.IsAccepted);
        Assert.Equal("type:a", result.Content!.TargetStableId);
    }
}
