using Archy.Features.Memory.ModelProviders.Contracts;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.IndexEmbeddings;

public interface IEmbeddingModelProviderResolver
{
    Result<IModelProvider> Resolve(string configuredProvider);
}
