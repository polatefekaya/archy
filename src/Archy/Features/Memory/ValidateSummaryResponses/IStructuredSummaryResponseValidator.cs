namespace Archy.Features.Memory.ValidateSummaryResponses;

public interface IStructuredSummaryResponseValidator
{
    SummaryResponseValidation Validate(SummaryResponseValidationRequest request);
}
