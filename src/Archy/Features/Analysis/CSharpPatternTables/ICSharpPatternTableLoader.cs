using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.CSharpPatternTables;

public interface ICSharpPatternTableLoader
{
    Result<CSharpPatternTable> Load(ArchyConfiguration configuration);
}
