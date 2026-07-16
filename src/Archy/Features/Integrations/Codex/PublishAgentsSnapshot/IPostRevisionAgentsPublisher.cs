using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Integrations.Codex.PublishAgentsSnapshot;

public interface IPostRevisionAgentsPublisher
{
    ValueTask<Result<bool>> PublishAsync(
        string repositoryRoot,
        ArchyConfiguration configuration,
        long graphRevision,
        CancellationToken cancellationToken);
}
