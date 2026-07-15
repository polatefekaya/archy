using System.Security.Cryptography;
using Archy.Features.Analysis.InventorySources;

namespace Archy.UnitTests.Features.Analysis.ExtractCSharpSyntaxFacts;

internal sealed class CSharpSyntaxFactFixture : IDisposable
{
    private CSharpSyntaxFactFixture(string repositoryRoot)
    {
        RepositoryRoot = repositoryRoot;
    }

    public string RepositoryRoot { get; }

    public static CSharpSyntaxFactFixture Create()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"archy-csharp-syntax-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryRoot);
        return new CSharpSyntaxFactFixture(repositoryRoot);
    }

    public async Task<SourceFile> WriteAsync(string repositoryRelativePath, string contents, SourceLanguage language = SourceLanguage.CSharp)
    {
        var fullPath = Path.Combine(RepositoryRoot, repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, contents);
        var bytes = await File.ReadAllBytesAsync(fullPath);
        return new SourceFile(
            repositoryRelativePath,
            language,
            Convert.ToHexString(SHA256.HashData(bytes)),
            bytes.LongLength);
    }

    public void Dispose()
    {
        if (Directory.Exists(RepositoryRoot))
        {
            Directory.Delete(RepositoryRoot, recursive: true);
        }
    }
}
