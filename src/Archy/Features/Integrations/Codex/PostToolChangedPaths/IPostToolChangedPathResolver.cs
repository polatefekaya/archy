using Archy.SharedKernel.Primitives;

namespace Archy.Features.Integrations.Codex.PostToolChangedPaths;

public interface IPostToolChangedPathResolver
{
    ValueTask<Result<PostToolChangedPathResolution>> ResolveAsync(
        PostToolChangedPathRequest request,
        CancellationToken cancellationToken);
}
