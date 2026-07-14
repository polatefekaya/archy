using Archy.SharedKernel.Primitives;

namespace Archy.Features.Configuration.LoadEffectiveConfiguration;

public interface ITomlConfigurationParser
{
    Result<ArchyConfigurationLayer> Parse(string document, string sourcePath);
}
