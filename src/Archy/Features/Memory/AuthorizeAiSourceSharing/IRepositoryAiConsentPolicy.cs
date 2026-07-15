using Archy.Features.Configuration.LoadEffectiveConfiguration;

namespace Archy.Features.Memory.AuthorizeAiSourceSharing;

public interface IRepositoryAiConsentPolicy
{
    AiSourceSharingDecision Evaluate(MemorySelectionConfiguration configuration, AiSourceSharingOperation operation);
}
