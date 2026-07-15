using Archy.Features.Configuration.LoadEffectiveConfiguration;

namespace Archy.Features.Memory.AuthorizeAiSourceSharing;

/// <summary>Credential availability is intentionally irrelevant: only explicit repository consent can authorize outbound source sharing.</summary>
public sealed class RepositoryAiConsentPolicy : IRepositoryAiConsentPolicy
{
    public AiSourceSharingDecision Evaluate(MemorySelectionConfiguration configuration, AiSourceSharingOperation operation)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        return (configuration.SourceSharing, operation) switch
        {
            (AiSourceSharingMode.SummariesOnly, AiSourceSharingOperation.Summary) => new(true, "Repository consent permits source-derived summaries."),
            (AiSourceSharingMode.SummariesAndEmbeddings, _) => new(true, "Repository consent permits source-derived summaries and embeddings."),
            (AiSourceSharingMode.SummariesOnly, AiSourceSharingOperation.Embedding) => new(false, "Repository consent permits summaries only; embeddings remain disabled."),
            _ => new(false, "Repository source sharing is disabled until archy.toml explicitly opts in."),
        };
    }
}
