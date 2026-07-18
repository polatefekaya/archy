using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Archy.Features.Memory.DetermineImportantNodes;

namespace Archy.Features.Duplicates.SelectEmbeddingChunks;

/// <summary>Selects a single C# declaration per eligible graph node without including unrelated file content.</summary>
public sealed class CSharpEmbeddingChunkSelector : IEmbeddingChunkSelector
{
    public IReadOnlyList<EmbeddingChunk> CreateChunks(ImportantNodeEligibility eligibility, IReadOnlyList<EmbeddingChunkSource> sources)
    {
        ArgumentNullException.ThrowIfNull(eligibility);
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Any(static source => source is null || string.IsNullOrWhiteSpace(source.MethodStableId) || string.IsNullOrWhiteSpace(source.RepositoryRelativePath) || source.StartLine < 1 || source.EndLine < source.StartLine || source.SourceText.Length > 2_000_000) ||
            sources.Select(static source => source.MethodStableId).Distinct(StringComparer.Ordinal).Count() != sources.Count)
        {
            throw new ArgumentException("Embedding chunks require unique bounded declaration source ranges.", nameof(sources));
        }

        var eligible = eligibility.Included
            .Where(static target => target.Kind is ImportantNodeTargetKind.PublicApi or ImportantNodeTargetKind.PublicType)
            .Select(static target => target.StableId)
            .ToHashSet(StringComparer.Ordinal);
        var chunks = new List<EmbeddingChunk>();
        foreach (var source in sources.Where(source => eligible.Contains(source.MethodStableId)).OrderBy(static source => source.MethodStableId, StringComparer.Ordinal))
        {
            var chunk = SelectDeclaration(source);
            if (chunk is not null)
            {
                chunks.Add(chunk);
            }
        }

        return [.. chunks];
    }

    private static EmbeddingChunk? SelectDeclaration(EmbeddingChunkSource source)
    {
        var tree = CSharpSyntaxTree.ParseText(source.SourceText, path: source.RepositoryRelativePath);
        var root = tree.GetRoot();
        var type = root.DescendantNodes()
            .OfType<BaseTypeDeclarationSyntax>()
            .Where(node => MatchesRange(tree, node, source.StartLine, source.EndLine))
            .OrderBy(static node => node.Span.Length)
            .FirstOrDefault();
        if (type is not null)
        {
            return CreateTypeChunk(source, type);
        }

        var method = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(node => MatchesRange(tree, node, source.StartLine, source.EndLine))
            .OrderBy(static node => node.Span.Length)
            .FirstOrDefault();
        return method is null ? null : CreateMethodChunk(source, method);
    }

    private static EmbeddingChunk? CreateMethodChunk(EmbeddingChunkSource source, MethodDeclarationSyntax method)
    {
        var documentation = string.Concat(method.GetLeadingTrivia()
            .Select(static trivia => trivia.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .Select(static comment => comment.ToFullString().Trim()));
        var signature = method
            .WithBody(null)
            .WithExpressionBody(null)
            .WithSemicolonToken(default)
            .NormalizeWhitespace()
            .ToFullString()
            .Trim();
        var body = method.Body?.NormalizeWhitespace().ToFullString().Trim() ?? method.ExpressionBody?.NormalizeWhitespace().ToFullString().Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var content = new StringBuilder()
            .Append("signature: ").AppendLine(signature)
            .Append("documentation: ").AppendLine(documentation)
            .Append("body:").AppendLine()
            .Append(body)
            .ToString();
        return new EmbeddingChunk(
            source.MethodStableId,
            source.RepositoryRelativePath,
            content,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))));
    }

    private static EmbeddingChunk CreateTypeChunk(EmbeddingChunkSource source, BaseTypeDeclarationSyntax type)
    {
        var documentation = string.Concat(type.GetLeadingTrivia()
            .Select(static trivia => trivia.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .Select(static comment => comment.ToFullString().Trim()));
        var content = new StringBuilder()
            .Append("declaration: ").AppendLine(type.Kind().ToString())
            .Append("documentation: ").AppendLine(documentation)
            .Append("source:").AppendLine()
            .Append(type.NormalizeWhitespace().ToFullString().Trim())
            .ToString();
        return new EmbeddingChunk(
            source.MethodStableId,
            source.RepositoryRelativePath,
            content,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))));
    }

    private static bool MatchesRange(SyntaxTree tree, SyntaxNode node, int startLine, int endLine)
    {
        var range = tree.GetLineSpan(node.Span);
        var nodeStart = range.StartLinePosition.Line + 1;
        var nodeEnd = range.EndLinePosition.Line + 1;
        return nodeStart >= startLine && nodeEnd <= endLine;
    }
}
