namespace Archy.Features.Analysis.InventorySources;

public sealed record SourceFile(
    string RepositoryRelativePath,
    SourceLanguage Language,
    string ContentHash,
    long ByteLength);
