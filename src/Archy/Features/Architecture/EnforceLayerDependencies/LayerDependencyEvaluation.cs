using Archy.Features.Architecture.LayerMembership;
using Archy.Features.Architecture.DetectDependencyCycles;
using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.Features.Architecture.EnforceLayerDependencies;

public sealed record LayerDependencyEvaluation(
    IReadOnlyList<LayerMembershipResolution> Membership,
    IReadOnlyList<LayerCoverageIssue> CoverageIssues,
    IReadOnlyList<ArchitectureDependencyCycle> Cycles,
    IReadOnlyList<LayerDependencyViolation> Violations,
    EnforcementReach Reach);

/// <summary>
/// How much of the active revision the hard-edge policy could actually act on.
/// </summary>
/// <remarks>
/// Enforcement only reads edges that are both confidence-<c>1.0</c> and of a configured kind.
/// A revision can be fully analyzed, have every node assigned to a layer, and still contain no
/// such edge — for example when semantic analysis was unavailable and only syntax-level facts
/// were recorded. The gate is then structurally incapable of failing, which is indistinguishable
/// from a clean repository unless it is stated. This record carries the counts needed to state it.
/// </remarks>
/// <param name="EligibleEdgeCount">Edges satisfying confidence and configured kind.</param>
/// <param name="TotalEdgeCount">Edges in the evaluated revision.</param>
/// <param name="PopulatedLayerCount">Configured layers that actually own at least one assigned node.</param>
/// <param name="ConfiguredEdgeKinds">Edge kinds the repository asked to enforce.</param>
/// <param name="ObservedEdgeKinds">Edge kinds actually present in the revision.</param>
public sealed record EnforcementReach(
    int EligibleEdgeCount,
    int TotalEdgeCount,
    int PopulatedLayerCount,
    IReadOnlyList<string> ConfiguredEdgeKinds,
    IReadOnlyList<string> ObservedEdgeKinds)
{
    /// <summary>
    /// True when a direction rule could apply but no edge is eligible to test it.
    /// </summary>
    /// <remarks>
    /// Two populated layers are required before silence is suspicious. A repository with a single
    /// populated layer, or with no dependencies at all, legitimately has nothing to enforce, and
    /// reporting that as a gap would be noise. Once two layers own code and not one edge is
    /// eligible, the gate is reporting on nothing while looking like it reported on everything.
    /// </remarks>
    public bool IsUnenforceable => PopulatedLayerCount >= 2 && EligibleEdgeCount == 0;
}

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
