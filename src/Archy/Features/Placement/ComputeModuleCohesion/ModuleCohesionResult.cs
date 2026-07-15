using Archy.Features.Placement.ClusterRevisions;
namespace Archy.Features.Placement.ComputeModuleCohesion;
public sealed record ModuleCohesionResult(ClusterRevisionFact ClusterRevision, IReadOnlyList<ModuleCohesionMetric> Metrics);
