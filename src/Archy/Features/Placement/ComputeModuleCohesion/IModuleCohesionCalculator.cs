using Archy.Features.Graph.ReadGraphRevision;
namespace Archy.Features.Placement.ComputeModuleCohesion;
public interface IModuleCohesionCalculator { ModuleCohesionResult Calculate(GraphRevisionSnapshot graph, IReadOnlyList<DetectedCommunity> communities, string algorithmVersion); }
