using Archy.Features.Architecture.EnforceLayerDependencies;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>Converts raw deterministic evaluation output into stable baseline identities.</summary>
public interface IArchitectureFindingFactory
{
    Result<IReadOnlyList<ArchitectureFinding>> Create(LayerDependencyEvaluation evaluation);
}
