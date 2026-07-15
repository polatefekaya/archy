using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Archy.Features.Analysis.InventorySources;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ResolveDotNetMessageContracts;

public sealed class DotNetMessageContractProvider : IDotNetMessageContractProvider
{
    private const string Provider = "dotnet-message-syntax";
    private static readonly HashSet<string> ProducerMethods = ["Publish", "Send"];

    public async ValueTask<Result<DotNetMessageContractFacts>> ResolveAsync(
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
        var edges = new Dictionary<string, GraphEdgeFact>(StringComparer.Ordinal);
        var diagnostics = new List<DotNetMessageContractDiagnostic>();
        var consumersByMessage = new Dictionary<string, List<ConsumerSite>>(StringComparer.Ordinal);
        var producers = new List<ProducerSite>();
        var repositoryFullPath = Path.GetFullPath(repositoryRoot);

        foreach (var file in files
                     .Where(static file => file.Language == SourceLanguage.CSharp)
                     .OrderBy(static file => file.RepositoryRelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = await ParseAsync(repositoryFullPath, file, typesBySimpleName, fileNodesByPath, cancellationToken);
            if (!parsed.IsSuccess)
            {
                return ResultFactory.Failure<DotNetMessageContractFacts>(parsed.Problem!);
            }

            producers.AddRange(parsed.Value.Producers);
            foreach (var consumer in parsed.Value.Consumers)
            {
                if (!consumersByMessage.TryGetValue(consumer.MessageType, out var consumers))
                {
                    consumers = [];
                    consumersByMessage.Add(consumer.MessageType, consumers);
                }

                consumers.Add(consumer);
            }

            diagnostics.AddRange(parsed.Value.Diagnostics);
        }

        foreach (var producer in producers)
        {
            if (!consumersByMessage.TryGetValue(producer.MessageType, out var consumers))
            {
                diagnostics.Add(new DotNetMessageContractDiagnostic(
                    producer.RepositoryRelativePath,
                    producer.Line,
                    producer.Column,
                    "unresolved_message_consumer",
                    $"Archy found {producer.Method}<{producer.MessageType}> but no unambiguous IConsumer<{producer.MessageType}> declaration."));
                continue;
            }

            foreach (var consumer in consumers)
            {
                var edgeKind = producer.Method == "Publish" ? "message_publish" : "message_send";
                var edgeId = EdgeId(producer.SourceStableId, consumer.ConsumerStableId, edgeKind, producer.MessageType);
                edges.TryAdd(edgeId, new GraphEdgeFact(
                    edgeId,
                    producer.SourceStableId,
                    consumer.ConsumerStableId,
                    edgeKind,
                    producer.MessageType,
                    Provider,
                    Confidence: 0.8,
                    EvidenceJson: JsonSerializer.Serialize(
                        new DotNetMessageContractEvidence(
                            producer.Method,
                            producer.MessageType,
                            producer.SourceStableId,
                            consumer.ConsumerStableId,
                            producer.RepositoryRelativePath,
                            producer.Line,
                            producer.Column),
                        DotNetMessageContractJsonContext.Default.DotNetMessageContractEvidence)));
            }
        }

        return ResultFactory.Success(new DotNetMessageContractFacts(
            [.. edges.Values.OrderBy(static edge => edge.EdgeId, StringComparer.Ordinal)],
            [.. diagnostics
                .OrderBy(static diagnostic => diagnostic.RepositoryRelativePath, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)]));
    }

    private static async Task<Result<ParsedMessageFile>> ParseAsync(
        string repositoryRoot,
        SourceFile file,
        Dictionary<string, GraphNodeFact[]> typesBySimpleName,
        IReadOnlyDictionary<string, GraphNodeFact> fileNodesByPath,
        CancellationToken cancellationToken)
    {
        var path = ResolveRepositoryFile(repositoryRoot, file.RepositoryRelativePath);
        if (!path.IsSuccess)
        {
            return ResultFactory.Failure<ParsedMessageFile>(path.Problem!);
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<ParsedMessageFile>(
                Problem.Storage($"Archy could not read message provider source '{file.RepositoryRelativePath}': {exception.Message}"));
        }

        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), file.ContentHash, StringComparison.Ordinal))
        {
            return ResultFactory.Failure<ParsedMessageFile>(
                Problem.Conflict($"C# source '{file.RepositoryRelativePath}' changed after source inventory. Run analysis again."));
        }

        var tree = CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(bytes), path: file.RepositoryRelativePath, cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken);
        var producers = new List<ProducerSite>();
        var consumers = new List<ConsumerSite>();
        var diagnostics = new List<DotNetMessageContractDiagnostic>();

        foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (!TryGetConsumerMessage(declaration, out var messageType) ||
                !TryResolveType(typesBySimpleName, GetSimpleTypeName(declaration.Identifier.ValueText), file.RepositoryRelativePath, out var consumer))
            {
                continue;
            }

            if (!TryResolveType(typesBySimpleName, messageType, repositoryRelativePath: null, out _))
            {
                var position = tree.GetLineSpan(declaration.Span, cancellationToken).StartLinePosition;
                diagnostics.Add(new DotNetMessageContractDiagnostic(
                    file.RepositoryRelativePath,
                    position.Line + 1,
                    position.Character + 1,
                    "unresolved_message_type",
                    $"Archy could not unambiguously resolve IConsumer<{messageType}> to a message declaration."));
                continue;
            }

            consumers.Add(new ConsumerSite(messageType, consumer.StableId));
        }

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!TryGetProducerMessage(invocation, out var method, out var messageType) ||
                !TryGetProducerNode(invocation, file.RepositoryRelativePath, typesBySimpleName, fileNodesByPath, out var producer))
            {
                continue;
            }

            if (!TryResolveType(typesBySimpleName, messageType, repositoryRelativePath: null, out _))
            {
                var unresolvedPosition = tree.GetLineSpan(invocation.Span, cancellationToken).StartLinePosition;
                diagnostics.Add(new DotNetMessageContractDiagnostic(
                    file.RepositoryRelativePath,
                    unresolvedPosition.Line + 1,
                    unresolvedPosition.Character + 1,
                    "unresolved_message_type",
                    $"Archy could not unambiguously resolve {method}<{messageType}> to a message declaration."));
                continue;
            }

            var position = tree.GetLineSpan(invocation.Span, cancellationToken).StartLinePosition;
            producers.Add(new ProducerSite(
                method,
                messageType,
                producer.StableId,
                file.RepositoryRelativePath,
                position.Line + 1,
                position.Character + 1));
        }

        return ResultFactory.Success(new ParsedMessageFile(producers, consumers, diagnostics));
    }

    private static bool TryGetConsumerMessage(TypeDeclarationSyntax declaration, out string messageType)
    {
        messageType = string.Empty;
        var contract = declaration.BaseList?.Types
            .Select(static baseType => GetTerminalGenericName(baseType.Type))
            .FirstOrDefault(static generic => generic is not null &&
                                      generic.Identifier.ValueText == "IConsumer" &&
                                      generic.TypeArgumentList.Arguments.Count == 1);
        if (contract is null)
        {
            return false;
        }

        messageType = GetSimpleTypeName(contract.TypeArgumentList.Arguments[0]);
        return messageType.Length > 0;
    }

    private static GenericNameSyntax? GetTerminalGenericName(TypeSyntax type) => type switch
    {
        GenericNameSyntax generic => generic,
        QualifiedNameSyntax qualified => GetTerminalGenericName(qualified.Right),
        AliasQualifiedNameSyntax aliasQualified => GetTerminalGenericName(aliasQualified.Name),
        _ => null,
    };

    private static bool TryGetProducerMessage(
        InvocationExpressionSyntax invocation,
        out string method,
        out string messageType)
    {
        method = string.Empty;
        messageType = string.Empty;
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName } ||
            !ProducerMethods.Contains(genericName.Identifier.ValueText) ||
            genericName.TypeArgumentList.Arguments.Count != 1)
        {
            return false;
        }

        method = genericName.Identifier.ValueText;
        messageType = GetSimpleTypeName(genericName.TypeArgumentList.Arguments[0]);
        return messageType.Length > 0;
    }

    private static bool TryGetProducerNode(
        InvocationExpressionSyntax invocation,
        string repositoryRelativePath,
        Dictionary<string, GraphNodeFact[]> typesBySimpleName,
        IReadOnlyDictionary<string, GraphNodeFact> fileNodesByPath,
        out GraphNodeFact node)
    {
        var containingType = invocation.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
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
        string? repositoryRelativePath,
        out GraphNodeFact node)
    {
        node = null!;
        if (!typesBySimpleName.TryGetValue(typeName, out var candidates))
        {
            return false;
        }

        var matching = repositoryRelativePath is null
            ? candidates
            : candidates.Where(candidate => string.Equals(candidate.FilePath, repositoryRelativePath, StringComparison.Ordinal)).ToArray();
        if (matching.Length != 1)
        {
            return false;
        }

        node = matching[0];
        return true;
    }

    private static Result<string> ResolveRepositoryFile(string repositoryRoot, string repositoryRelativePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var fullPath = Path.GetFullPath(Path.Combine(root, repositoryRelativePath));
        return Path.IsPathRooted(repositoryRelativePath) ||
               !fullPath.StartsWith(string.Concat(root, Path.DirectorySeparatorChar), StringComparison.Ordinal)
            ? ResultFactory.Failure<string>(Problem.Validation($"Message provider source path '{repositoryRelativePath}' escapes the repository root."))
            : ResultFactory.Success(fullPath);
    }

    private static string GetSimpleTypeName(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        QualifiedNameSyntax qualified => GetSimpleTypeName(qualified.Right),
        AliasQualifiedNameSyntax aliasQualified => GetSimpleTypeName(aliasQualified.Name),
        NullableTypeSyntax nullable => GetSimpleTypeName(nullable.ElementType),
        _ => string.Empty,
    };

    private static string GetSimpleTypeName(string fullyQualifiedName)
    {
        var lastSegment = fullyQualifiedName.Split('.').LastOrDefault() ?? string.Empty;
        var arityIndex = lastSegment.IndexOf('`', StringComparison.Ordinal);
        return arityIndex < 0 ? lastSegment : lastSegment[..arityIndex];
    }

    private static string EdgeId(string sourceStableId, string targetStableId, string edgeKind, string joinKey) =>
        $"edge:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceStableId}\u001f{targetStableId}\u001f{edgeKind}\u001f{joinKey}")))}";

    private sealed record ProducerSite(
        string Method,
        string MessageType,
        string SourceStableId,
        string RepositoryRelativePath,
        int Line,
        int Column);

    private sealed record ConsumerSite(string MessageType, string ConsumerStableId);

    private sealed record ParsedMessageFile(
        IReadOnlyList<ProducerSite> Producers,
        IReadOnlyList<ConsumerSite> Consumers,
        IReadOnlyList<DotNetMessageContractDiagnostic> Diagnostics);
}
