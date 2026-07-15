namespace Archy.Features.Memory.ValidateSummaryResponses;

public sealed record SummaryResponseValidation(
    SummaryResponseValidationDisposition Disposition,
    ValidatedSummaryContent? Content,
    string? FailureMessage)
{
    public bool IsAccepted => Disposition == SummaryResponseValidationDisposition.Accepted && Content is not null;
}
