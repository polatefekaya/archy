using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Archy.Features.Analysis.CSharpPatternTables;
using Archy.Features.Analysis.InventorySources;
using Archy.Features.Analysis.ProviderSiteMatches;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.MatchCSharpPatternSites;

public sealed class CSharpPatternSiteMatcher : ICSharpPatternSiteMatcher
{
    public async ValueTask<Result<IReadOnlyList<ProviderSiteMatch>>> MatchAsync(
        string repositoryRoot,
        IReadOnlyList<SourceFile> files,
        CSharpPatternTable patternTable,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(patternTable);

        var invalidTable = ValidatePatternTable(patternTable);
        if (invalidTable is not null)
        {
            return ResultFactory.Failure<IReadOnlyList<ProviderSiteMatch>>(invalidTable);
        }

        var matches = new List<ProviderSiteMatch>();
        var root = Path.GetFullPath(repositoryRoot);
        foreach (var file in files.Where(static file => file.Language == SourceLanguage.CSharp).OrderBy(static file => file.RepositoryRelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = await ParseAsync(root, file, cancellationToken);
            if (!parsed.IsSuccess)
            {
                return ResultFactory.Failure<IReadOnlyList<ProviderSiteMatch>>(parsed.Problem!);
            }

            foreach (var pattern in patternTable.Patterns)
            {
                AddMatches(pattern, parsed.Value, matches, cancellationToken);
            }
        }

        return ResultFactory.Success<IReadOnlyList<ProviderSiteMatch>>(
            [.. matches
                .OrderBy(static match => match.Evidence.RepositoryRelativePath, StringComparer.Ordinal)
                .ThenBy(static match => match.Evidence.StartLine)
                .ThenBy(static match => match.Evidence.StartColumn)
                .ThenBy(static match => match.ProviderId, StringComparer.Ordinal)]);
    }

    private static async Task<Result<ParsedCSharpFile>> ParseAsync(string repositoryRoot, SourceFile file, CancellationToken cancellationToken)
    {
        var path = ResolveRepositoryFile(repositoryRoot, file.RepositoryRelativePath);
        if (!path.IsSuccess)
        {
            return ResultFactory.Failure<ParsedCSharpFile>(path.Problem!);
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<ParsedCSharpFile>(Problem.Storage($"Archy could not read pattern source '{file.RepositoryRelativePath}': {exception.Message}"));
        }

        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), file.ContentHash, StringComparison.Ordinal))
        {
            return ResultFactory.Failure<ParsedCSharpFile>(Problem.Conflict($"C# source '{file.RepositoryRelativePath}' changed after source inventory. Run analysis again."));
        }

        var tree = CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(bytes), path: file.RepositoryRelativePath, cancellationToken: cancellationToken);
        return ResultFactory.Success(new ParsedCSharpFile(file, tree, await tree.GetRootAsync(cancellationToken)));
    }

    private static void AddMatches(CSharpPatternDefinition pattern, ParsedCSharpFile file, List<ProviderSiteMatch> matches, CancellationToken cancellationToken)
    {
        switch (pattern.MatchKind)
        {
            case CSharpPatternMatchKind.Invocation:
                foreach (var invocation in file.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (!string.Equals(GetInvokedMemberName(invocation.Expression), pattern.Member, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (pattern.TypeConstraint is not null)
                    {
                        Add(matches, pattern, file, invocation, [], ProviderSiteMatchState.Degraded,
                            new ProviderSiteMatchDiagnostic("semantic_receiver_required", $"Pattern '{pattern.Id}' requires receiver type '{pattern.TypeConstraint}', which syntax-only matching cannot prove."), cancellationToken);
                        continue;
                    }

                    var captures = CaptureInvocation(pattern, invocation, out var diagnostic);
                    Add(matches, pattern, file, invocation, captures, diagnostic is null ? ProviderSiteMatchState.Matched : ProviderSiteMatchState.Unresolved, diagnostic, cancellationToken);
                }

                break;
            case CSharpPatternMatchKind.Type:
                foreach (var declaration in file.Root.DescendantNodes().OfType<TypeDeclarationSyntax>().Where(declaration => string.Equals(declaration.Identifier.ValueText, pattern.Member, StringComparison.Ordinal)))
                {
                    Add(matches, pattern, file, declaration, [], ProviderSiteMatchState.Matched, null, cancellationToken);
                }

                break;
            case CSharpPatternMatchKind.Attribute:
                foreach (var attribute in file.Root.DescendantNodes().OfType<AttributeSyntax>().Where(attribute => AttributeName(attribute) == pattern.Member || AttributeName(attribute) == string.Concat(pattern.Member, "Attribute")))
                {
                    Add(matches, pattern, file, attribute, [], ProviderSiteMatchState.Matched, null, cancellationToken);
                }

                break;
        }
    }

    private static IReadOnlyList<ProviderSiteCapture> CaptureInvocation(CSharpPatternDefinition pattern, InvocationExpressionSyntax invocation, out ProviderSiteMatchDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (pattern.Capture is null)
        {
            return [];
        }

        if (invocation.ArgumentList.Arguments.Count <= pattern.Capture.ArgumentIndex)
        {
            diagnostic = new ProviderSiteMatchDiagnostic("capture_unavailable", $"Pattern '{pattern.Id}' requires argument {pattern.Capture.ArgumentIndex}, but the invocation has fewer arguments.");
            return [];
        }

        var expression = invocation.ArgumentList.Arguments[pattern.Capture.ArgumentIndex].Expression;
        return [new ProviderSiteCapture(pattern.Capture.Name, CaptureKind(expression), CaptureValue(expression))];
    }

    private static void Add(List<ProviderSiteMatch> matches, CSharpPatternDefinition pattern, ParsedCSharpFile file, SyntaxNode node, IReadOnlyList<ProviderSiteCapture> captures, ProviderSiteMatchState state, ProviderSiteMatchDiagnostic? diagnostic, CancellationToken cancellationToken)
    {
        var range = file.Tree.GetLineSpan(node.Span, cancellationToken);
        var result = ProviderSiteMatchContract.Create(
            pattern.Id,
            "csharp",
            pattern.Framework,
            $"{pattern.MatchKind.ToString().ToLowerInvariant()}:{pattern.Member}",
            new ProviderSiteEvidence(file.File.RepositoryRelativePath, file.File.ContentHash, range.StartLinePosition.Line + 1, range.StartLinePosition.Character + 1, range.EndLinePosition.Line + 1, range.EndLinePosition.Character + 1),
            captures,
            state,
            diagnostic);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Problem!.Message);
        }

        matches.Add(result.Value);
    }

    private static Problem? ValidatePatternTable(CSharpPatternTable table) =>
        table.Patterns is null || table.Patterns.Any(static pattern => pattern is null || string.IsNullOrWhiteSpace(pattern.Id) || string.IsNullOrWhiteSpace(pattern.Framework) || string.IsNullOrWhiteSpace(pattern.Member))
            ? Problem.Validation("C# pattern tables require complete pattern definitions.")
            : null;

    private static string GetInvokedMemberName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        _ => string.Empty,
    };

    private static string AttributeName(AttributeSyntax attribute) => attribute.Name switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
        _ => attribute.Name.ToString(),
    };

    private static ProviderSiteCaptureKind CaptureKind(ExpressionSyntax expression) => expression is LiteralExpressionSyntax
        ? ProviderSiteCaptureKind.Literal
        : expression is IdentifierNameSyntax
            ? ProviderSiteCaptureKind.Identifier
            : ProviderSiteCaptureKind.Metadata;

    private static string CaptureValue(ExpressionSyntax expression) => expression is LiteralExpressionSyntax literal
        ? literal.Token.ValueText
        : expression.ToString();

    private static Result<string> ResolveRepositoryFile(string repositoryRoot, string repositoryRelativePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var fullPath = Path.GetFullPath(Path.Combine(root, repositoryRelativePath));
        return Path.IsPathRooted(repositoryRelativePath) || !fullPath.StartsWith(string.Concat(root, Path.DirectorySeparatorChar), StringComparison.Ordinal)
            ? ResultFactory.Failure<string>(Problem.Validation($"C# pattern source path '{repositoryRelativePath}' escapes the repository root."))
            : ResultFactory.Success(fullPath);
    }

    private sealed record ParsedCSharpFile(SourceFile File, SyntaxTree Tree, SyntaxNode Root);
}
