namespace Archy.Features.Duplicates.CompareInterfaceSignatures;

public interface IInterfaceSignatureSimilarityScorer
{
    InterfaceSignatureSimilarity Score(InterfaceSignature left, InterfaceSignature right);
}
