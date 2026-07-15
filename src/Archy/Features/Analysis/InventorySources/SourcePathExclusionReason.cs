namespace Archy.Features.Analysis.InventorySources;

public enum SourcePathExclusionReason
{
    GitMetadata,
    ArchyState,
    GeneratedDirectory,
    GitIgnore,
    ScopeInclude,
    ScopeExclude,
    SymbolicLink,
}
