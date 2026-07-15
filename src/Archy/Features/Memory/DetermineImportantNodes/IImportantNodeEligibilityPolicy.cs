using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Graph.ReadGraphRevision;

namespace Archy.Features.Memory.DetermineImportantNodes;

public interface IImportantNodeEligibilityPolicy
{
    ImportantNodeEligibility Determine(
        GraphRevisionSnapshot snapshot,
        MemorySelectionConfiguration configuration);
}
