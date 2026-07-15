namespace Archy.Features.Analysis.InventorySources;

public sealed record SourceInventoryDiagnostic(
    string RepositoryRelativePath,
    string Code,
    string Message);
