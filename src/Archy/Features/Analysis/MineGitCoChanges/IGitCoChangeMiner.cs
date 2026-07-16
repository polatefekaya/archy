using Archy.SharedKernel.Primitives;
namespace Archy.Features.Analysis.MineGitCoChanges;
public interface IGitCoChangeMiner { ValueTask<Result<IReadOnlyList<CoChangeEdge>>> MineAsync(CoChangeMiningRequest request, CancellationToken cancellationToken); }
