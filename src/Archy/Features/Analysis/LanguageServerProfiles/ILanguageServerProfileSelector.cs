using Archy.Features.Configuration.LoadEffectiveConfiguration;

namespace Archy.Features.Analysis.LanguageServerProfiles;

public interface ILanguageServerProfileSelector
{
    LanguageServerProfileSelection Resolve(
        string repositoryRoot,
        LanguageServerProfileConfiguration profile,
        bool hasMatchingDocuments);
}
