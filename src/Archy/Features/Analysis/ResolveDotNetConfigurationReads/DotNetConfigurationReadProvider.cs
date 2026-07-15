using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Archy.Features.Analysis.ConfigurationKeys;
using Archy.Features.Analysis.InventorySources;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ResolveDotNetConfigurationReads;

public sealed class DotNetConfigurationReadProvider : IDotNetConfigurationReadProvider
{
    private const string Provider = "dotnet-configuration-syntax";
    private const double Confidence = 0.8;

    public async ValueTask<Result<DotNetConfigurationReadFacts>> ResolveAsync(
        string repositoryRoot,
        IReadOnlyList<SourceFile> files,
        IReadOnlyList<GraphNodeFact> nodes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(nodes);

        var typesBySimpleName = nodes
            .Where(static node => node.NodeKind is "class" or "struct" or "interface" or "record" or "record_struct")
            .GroupBy(static node => GetSimpleTypeName(node.DisplayName), StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static node => node.StableId, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var fileNodesByPath = nodes
            .Where(static node => node.NodeKind == "file" && node.FilePath is not null)
            .ToDictionary(static node => node.FilePath!, StringComparer.Ordinal);
        var reads = new List<ConfigurationReadSite>();
        var diagnostics = new List<DotNetConfigurationReadDiagnostic>();
        var repositoryFullPath = Path.GetFullPath(repositoryRoot);

        foreach (var file in files
                     .Where(static file => file.Language == SourceLanguage.CSharp)
                     .OrderBy(static file => file.RepositoryRelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = await ParseAsync(
                repositoryFullPath,
                file,
                typesBySimpleName,
                fileNodesByPath,
                cancellationToken);
            if (!parsed.IsSuccess)
            {
                return ResultFactory.Failure<DotNetConfigurationReadFacts>(parsed.Problem!);
            }

            reads.AddRange(parsed.Value.Reads);
            diagnostics.AddRange(parsed.Value.Diagnostics);
        }

        var configurationNodes = BuildConfigurationNodes(reads);
        var edges = BuildReadEdges(reads);
        return ResultFactory.Success(new DotNetConfigurationReadFacts(
            configurationNodes,
            edges,
            [.. diagnostics
                .OrderBy(static diagnostic => diagnostic.RepositoryRelativePath, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)]));
    }

    private static async Task<Result<ParsedConfigurationFile>> ParseAsync(
        string repositoryRoot,
        SourceFile file,
        Dictionary<string, GraphNodeFact[]> typesBySimpleName,
        IReadOnlyDictionary<string, GraphNodeFact> fileNodesByPath,
        CancellationToken cancellationToken)
    {
        var path = ResolveRepositoryFile(repositoryRoot, file.RepositoryRelativePath);
        if (!path.IsSuccess)
        {
            return ResultFactory.Failure<ParsedConfigurationFile>(path.Problem!);
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<ParsedConfigurationFile>(
                Problem.Storage($"Archy could not read configuration provider source '{file.RepositoryRelativePath}': {exception.Message}"));
        }

        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), file.ContentHash, StringComparison.Ordinal))
        {
            return ResultFactory.Failure<ParsedConfigurationFile>(
                Problem.Conflict($"C# source '{file.RepositoryRelativePath}' changed after source inventory. Run analysis again."));
        }

        var tree = CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(bytes), path: file.RepositoryRelativePath, cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken);
        var reads = new List<ConfigurationReadSite>();
        var diagnostics = new List<DotNetConfigurationReadDiagnostic>();

        foreach (var elementAccess in root.DescendantNodes().OfType<ElementAccessExpressionSyntax>())
        {
            if (!TryGetConfigurationPath(elementAccess.Expression, out var prefix))
            {
                continue;
            }

            if (!TryGetSingleStringArgument(elementAccess.ArgumentList.Arguments, out var rawKey))
            {
                AddDynamicKeyDiagnostic(tree, file.RepositoryRelativePath, elementAccess, "indexer", diagnostics, cancellationToken);
                continue;
            }

            AddRead("indexer", rawKey, prefix, elementAccess, tree, file.RepositoryRelativePath, typesBySimpleName, fileNodesByPath, reads, diagnostics, cancellationToken);
        }

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var methodName = GetInvokedMethodName(invocation.Expression);
            if (string.Equals(methodName, "GetEnvironmentVariable", StringComparison.Ordinal) &&
                IsEnvironmentInvocation(invocation))
            {
                if (!TryGetFirstStringArgument(invocation.ArgumentList.Arguments, out var rawKey))
                {
                    AddDynamicKeyDiagnostic(tree, file.RepositoryRelativePath, invocation, "environment", diagnostics, cancellationToken);
                    continue;
                }

                AddRead("environment", rawKey, string.Empty, invocation, tree, file.RepositoryRelativePath, typesBySimpleName, fileNodesByPath, reads, diagnostics, cancellationToken);
                continue;
            }

            if (string.Equals(methodName, "GetSection", StringComparison.Ordinal) &&
                TryGetConfigurationReceiver(invocation.Expression, out _) &&
                !TryGetSingleStringArgument(invocation.ArgumentList.Arguments, out _))
            {
                AddDynamicKeyDiagnostic(tree, file.RepositoryRelativePath, invocation, "section", diagnostics, cancellationToken);
                continue;
            }

            if (!TryGetConfigurationInvocationReceiver(invocation.Expression, out var receiverPath))
            {
                continue;
            }

            switch (methodName)
            {
                case "GetValue":
                    if (!TryGetFirstStringArgument(invocation.ArgumentList.Arguments, out var getValueKey))
                    {
                        AddDynamicKeyDiagnostic(tree, file.RepositoryRelativePath, invocation, "get_value", diagnostics, cancellationToken);
                        continue;
                    }

                    AddRead("get_value", getValueKey, receiverPath, invocation, tree, file.RepositoryRelativePath, typesBySimpleName, fileNodesByPath, reads, diagnostics, cancellationToken);
                    break;
                case "Bind" when receiverPath.Length > 0:
                    AddRead("bind", rawKey: receiverPath, prefix: string.Empty, invocation, tree, file.RepositoryRelativePath, typesBySimpleName, fileNodesByPath, reads, diagnostics, cancellationToken);
                    break;
                case "Get" when receiverPath.Length > 0:
                    AddRead("bind_get", rawKey: receiverPath, prefix: string.Empty, invocation, tree, file.RepositoryRelativePath, typesBySimpleName, fileNodesByPath, reads, diagnostics, cancellationToken);
                    break;
            }
        }

        return ResultFactory.Success(new ParsedConfigurationFile(reads, diagnostics));
    }

    private static IReadOnlyList<GraphNodeFact> BuildConfigurationNodes(IReadOnlyList<ConfigurationReadSite> reads) =>
        [.. reads
            .GroupBy(static read => read.KeyPath, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group =>
            {
                var evidenceReads = group
                    .OrderBy(static read => read.SourceStableId, StringComparer.Ordinal)
                    .ThenBy(static read => read.RepositoryRelativePath, StringComparer.Ordinal)
                    .ThenBy(static read => read.Line)
                    .ThenBy(static read => read.Column)
                    .ThenBy(static read => read.AccessForm, StringComparer.Ordinal)
                    .Select(static read => new DotNetConfigurationReadEvidence(
                        read.AccessForm,
                        read.KeyPath,
                        read.RawKey,
                        read.SourceStableId,
                        read.RepositoryRelativePath,
                        read.Line,
                        read.Column))
                    .ToArray();
                var evidence = JsonSerializer.Serialize(
                    new DotNetConfigurationKeyEvidence(group.Key, evidenceReads),
                    DotNetConfigurationReadJsonContext.Default.DotNetConfigurationKeyEvidence);
                return new GraphNodeFact(
                    ConfigurationKeyPath.StableId(group.Key),
                    "configuration_key",
                    $"configuration:key:{group.Key}",
                    group.Key,
                    FilePath: null,
                    StartLine: null,
                    EndLine: null,
                    Provider,
                    Confidence,
                    EvidenceJson: evidence,
                    ContentHash: Hash(evidence));
            })];

    private static IReadOnlyList<GraphEdgeFact> BuildReadEdges(IReadOnlyList<ConfigurationReadSite> reads)
    {
        var edges = new Dictionary<string, GraphEdgeFact>(StringComparer.Ordinal);
        foreach (var read in reads)
        {
            var joinKey = $"{read.AccessForm}:{read.KeyPath}";
            var edgeId = EdgeId(read.SourceStableId, ConfigurationKeyPath.StableId(read.KeyPath), "configuration_read", joinKey);
            edges.TryAdd(edgeId, new GraphEdgeFact(
                edgeId,
                read.SourceStableId,
                ConfigurationKeyPath.StableId(read.KeyPath),
                "configuration_read",
                joinKey,
                Provider,
                Confidence,
                EvidenceJson: JsonSerializer.Serialize(
                    new DotNetConfigurationReadEvidence(
                        read.AccessForm,
                        read.KeyPath,
                        read.RawKey,
                        read.SourceStableId,
                        read.RepositoryRelativePath,
                        read.Line,
                        read.Column),
                    DotNetConfigurationReadJsonContext.Default.DotNetConfigurationReadEvidence)));
        }

        return [.. edges.Values.OrderBy(static edge => edge.EdgeId, StringComparer.Ordinal)];
    }

    private static void AddRead(
        string accessForm,
        string rawKey,
        string prefix,
        SyntaxNode site,
        SyntaxTree tree,
        string repositoryRelativePath,
        Dictionary<string, GraphNodeFact[]> typesBySimpleName,
        IReadOnlyDictionary<string, GraphNodeFact> fileNodesByPath,
        List<ConfigurationReadSite> reads,
        List<DotNetConfigurationReadDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!ConfigurationKeyPath.TryCombine(prefix, rawKey, out var keyPath))
        {
            var position = tree.GetLineSpan(site.Span, cancellationToken).StartLinePosition;
            diagnostics.Add(new DotNetConfigurationReadDiagnostic(
                repositoryRelativePath,
                position.Line + 1,
                position.Character + 1,
                "invalid_configuration_key",
                $"Archy could not normalize the static configuration key '{rawKey}'."));
            return;
        }

        if (!TryGetSourceNode(site, repositoryRelativePath, typesBySimpleName, fileNodesByPath, out var source))
        {
            return;
        }

        var sourcePosition = tree.GetLineSpan(site.Span, cancellationToken).StartLinePosition;
        reads.Add(new ConfigurationReadSite(
            source.StableId,
            keyPath,
            rawKey,
            accessForm,
            repositoryRelativePath,
            sourcePosition.Line + 1,
            sourcePosition.Character + 1));
    }

    private static bool TryGetConfigurationInvocationReceiver(ExpressionSyntax expression, out string path)
    {
        path = string.Empty;
        return expression is MemberAccessExpressionSyntax memberAccess &&
               TryGetConfigurationPath(memberAccess.Expression, out path);
    }

    private static bool TryGetConfigurationReceiver(ExpressionSyntax expression, out ExpressionSyntax receiver)
    {
        receiver = null!;
        if (expression is not MemberAccessExpressionSyntax memberAccess ||
            !string.Equals(memberAccess.Name.Identifier.ValueText, "GetSection", StringComparison.Ordinal))
        {
            return false;
        }

        receiver = memberAccess.Expression;
        return TryGetConfigurationPath(receiver, out _);
    }

    private static bool TryGetConfigurationPath(ExpressionSyntax expression, out string path)
    {
        expression = StripParentheses(expression);
        if (IsKnownConfigurationRoot(expression))
        {
            path = string.Empty;
            return true;
        }

        if (expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess } invocation &&
            string.Equals(memberAccess.Name.Identifier.ValueText, "GetSection", StringComparison.Ordinal) &&
            TryGetConfigurationPath(memberAccess.Expression, out var prefix) &&
            TryGetSingleStringArgument(invocation.ArgumentList.Arguments, out var section) &&
            ConfigurationKeyPath.TryCombine(prefix, section, out path))
        {
            return true;
        }

        if (expression is ElementAccessExpressionSyntax elementAccess &&
            TryGetConfigurationPath(elementAccess.Expression, out var elementPrefix) &&
            TryGetSingleStringArgument(elementAccess.ArgumentList.Arguments, out var key) &&
            ConfigurationKeyPath.TryCombine(elementPrefix, key, out path))
        {
            return true;
        }

        path = string.Empty;
        return false;
    }

    private static bool IsKnownConfigurationRoot(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText is "configuration" or "config",
        MemberAccessExpressionSyntax memberAccess => string.Equals(memberAccess.Name.Identifier.ValueText, "Configuration", StringComparison.Ordinal),
        _ => false,
    };

    private static bool IsEnvironmentInvocation(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
        string.Equals(GetSimpleName(memberAccess.Expression), "Environment", StringComparison.Ordinal);

    private static string GetSimpleName(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
        _ => string.Empty,
    };

    private static string GetInvokedMethodName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        _ => string.Empty,
    };

    private static bool TryGetSingleStringArgument(SeparatedSyntaxList<ArgumentSyntax> arguments, out string value)
    {
        value = string.Empty;
        return arguments.Count == 1 && TryGetStaticString(arguments[0].Expression, out value);
    }

    private static bool TryGetFirstStringArgument(SeparatedSyntaxList<ArgumentSyntax> arguments, out string value)
    {
        value = string.Empty;
        return arguments.Count > 0 && TryGetStaticString(arguments[0].Expression, out value);
    }

    private static bool TryGetStaticString(ExpressionSyntax expression, out string value)
    {
        value = string.Empty;
        if (expression is LiteralExpressionSyntax { RawKind: (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression } literal)
        {
            value = literal.Token.ValueText;
            return true;
        }

        if (expression is InterpolatedStringExpressionSyntax { Contents.Count: 1 } interpolated &&
            interpolated.Contents[0] is InterpolatedStringTextSyntax text)
        {
            value = text.TextToken.ValueText;
            return true;
        }

        return false;
    }

    private static bool TryGetSourceNode(
        SyntaxNode site,
        string repositoryRelativePath,
        Dictionary<string, GraphNodeFact[]> typesBySimpleName,
        IReadOnlyDictionary<string, GraphNodeFact> fileNodesByPath,
        out GraphNodeFact node)
    {
        var containingType = site.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (containingType is not null &&
            TryResolveType(typesBySimpleName, containingType.Identifier.ValueText, repositoryRelativePath, out node))
        {
            return true;
        }

        return fileNodesByPath.TryGetValue(repositoryRelativePath, out node!);
    }

    private static bool TryResolveType(
        Dictionary<string, GraphNodeFact[]> typesBySimpleName,
        string typeName,
        string repositoryRelativePath,
        out GraphNodeFact node)
    {
        node = null!;
        if (!typesBySimpleName.TryGetValue(typeName, out var candidates))
        {
            return false;
        }

        var matching = candidates
            .Where(candidate => string.Equals(candidate.FilePath, repositoryRelativePath, StringComparison.Ordinal))
            .ToArray();
        if (matching.Length != 1)
        {
            return false;
        }

        node = matching[0];
        return true;
    }

    private static void AddDynamicKeyDiagnostic(
        SyntaxTree tree,
        string repositoryRelativePath,
        SyntaxNode site,
        string accessForm,
        List<DotNetConfigurationReadDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var position = tree.GetLineSpan(site.Span, cancellationToken).StartLinePosition;
        diagnostics.Add(new DotNetConfigurationReadDiagnostic(
            repositoryRelativePath,
            position.Line + 1,
            position.Character + 1,
            "dynamic_configuration_key",
            $"Archy found a {accessForm} configuration read with a non-static key. It is intentionally not converted into a guessed configuration edge."));
    }

    private static Result<string> ResolveRepositoryFile(string repositoryRoot, string repositoryRelativePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var fullPath = Path.GetFullPath(Path.Combine(root, repositoryRelativePath));
        return Path.IsPathRooted(repositoryRelativePath) ||
               !fullPath.StartsWith(string.Concat(root, Path.DirectorySeparatorChar), StringComparison.Ordinal)
            ? ResultFactory.Failure<string>(Problem.Validation($"Configuration provider source path '{repositoryRelativePath}' escapes the repository root."))
            : ResultFactory.Success(fullPath);
    }

    private static ExpressionSyntax StripParentheses(ExpressionSyntax expression) => expression is ParenthesizedExpressionSyntax parenthesized
        ? StripParentheses(parenthesized.Expression)
        : expression;

    private static string GetSimpleTypeName(string fullyQualifiedName)
    {
        var lastSegment = fullyQualifiedName.Split('.').LastOrDefault() ?? string.Empty;
        var arityIndex = lastSegment.IndexOf('`', StringComparison.Ordinal);
        return arityIndex < 0 ? lastSegment : lastSegment[..arityIndex];
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string EdgeId(string sourceStableId, string targetStableId, string edgeKind, string joinKey) =>
        $"edge:{Hash($"{sourceStableId}\u001f{targetStableId}\u001f{edgeKind}\u001f{joinKey}")}";

    private sealed record ConfigurationReadSite(
        string SourceStableId,
        string KeyPath,
        string RawKey,
        string AccessForm,
        string RepositoryRelativePath,
        int Line,
        int Column);

    private sealed record ParsedConfigurationFile(
        IReadOnlyList<ConfigurationReadSite> Reads,
        IReadOnlyList<DotNetConfigurationReadDiagnostic> Diagnostics);
}
