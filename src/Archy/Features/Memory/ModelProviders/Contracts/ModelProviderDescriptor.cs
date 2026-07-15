namespace Archy.Features.Memory.ModelProviders.Contracts;

public sealed record ModelProviderDescriptor(
    string ProviderId,
    bool SupportsStructuredSummaries,
    bool SupportsEmbeddings);
