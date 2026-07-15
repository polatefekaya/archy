namespace Archy.Features.Memory.ModelProviders.Contracts;

public sealed record ModelResponseMetadata(
    string ProviderRequestId,
    string MetadataJson);
