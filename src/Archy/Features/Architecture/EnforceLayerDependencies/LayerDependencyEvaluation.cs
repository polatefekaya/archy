using Archy.Features.Architecture.LayerMembership;
using Archy.Features.Architecture.DetectDependencyCycles;
using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.Features.Architecture.EnforceLayerDependencies;

public sealed record LayerDependencyEvaluation(
    IReadOnlyList<LayerMembershipResolution> Membership,
    IReadOnlyList<LayerCoverageIssue> CoverageIssues,
    IReadOnlyList<ArchitectureDependencyCycle> Cycles,
    IReadOnlyList<LayerDependencyViolation> Violations);

/// <summary>A code node that cannot participate in sound layer enforcement yet.</summary>
public sealed record LayerCoverageIssue(
    string NodeStableId,
    LayerMembershipState State,
    string Message);

/// <summary>One hard, evidence-preserving layer-direction violation.</summary>
public sealed record LayerDependencyViolation(
    string EdgeId,
    string SourceLayer,
    string TargetLayer,
    GraphEdgeFact Edge,
    string Message);
