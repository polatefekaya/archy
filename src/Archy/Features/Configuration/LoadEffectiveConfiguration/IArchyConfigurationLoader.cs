using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Configuration.LoadEffectiveConfiguration;

public interface IArchyConfigurationLoader
{
    ValueTask<Result<EffectiveConfiguration>> LoadAsync(
        LocatedWorkspace workspace,
        string? explicitConfigurationPath,
        string? stateRootOverride,
        CancellationToken cancellationToken);
}
