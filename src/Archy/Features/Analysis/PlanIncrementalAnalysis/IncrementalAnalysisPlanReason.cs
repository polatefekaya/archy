namespace Archy.Features.Analysis.PlanIncrementalAnalysis;

public enum IncrementalAnalysisPlanReason
{
    InitialScan,
    NoSourceChanges,
    IncrementalReuse,
    SnapshotCoverageUnavailable,
}
