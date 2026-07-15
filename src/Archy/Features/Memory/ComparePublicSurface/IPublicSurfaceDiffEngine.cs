using Archy.Features.Graph.ReadGraphRevision;

namespace Archy.Features.Memory.ComparePublicSurface;

public interface IPublicSurfaceDiffEngine
{
    PublicSurfaceDiff Compare(GraphRevisionSnapshot prior, GraphRevisionSnapshot current);
}
