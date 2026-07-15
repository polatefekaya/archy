namespace Archy.Features.Duplicates.CompareInterfaceSignatures;

public sealed record InterfaceSignatureSimilarity(
    string LeftSymbolId,
    string RightSymbolId,
    double OverallScore,
    double ParameterTypeScore,
    double ReturnTypeScore,
    double GenericArityScore,
    double NameTokenScore);
