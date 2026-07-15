namespace Archy.Features.Duplicates.AggregateDuplicateSignals;

public interface IDuplicateSignalAggregator
{
    DuplicateAggregationResult Aggregate(DuplicateSignalSet signals);
}
