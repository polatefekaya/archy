using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Archy.Features.Analysis.InventorySources;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ResolveRabbitMqTopology;

public sealed class RabbitMqTopologyProvider : IRabbitMqTopologyProvider
{
    private const string Provider = "rabbitmq-syntax";
    private const double Confidence = 0.65;

    public async ValueTask<Result<RabbitMqTopologyFacts>> ResolveAsync(
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
        var operations = new List<RabbitMqOperation>();
        var diagnostics = new List<RabbitMqTopologyDiagnostic>();
        var repositoryFullPath = Path.GetFullPath(repositoryRoot);

        foreach (var file in files
                     .Where(static file => file.Language == SourceLanguage.CSharp)
                     .OrderBy(static file => file.RepositoryRelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = await ParseAsync(repositoryFullPath, file, typesBySimpleName, fileNodesByPath, cancellationToken);
            if (!parsed.IsSuccess)
            {
                return ResultFactory.Failure<RabbitMqTopologyFacts>(parsed.Problem!);
            }

            operations.AddRange(parsed.Value.Operations);
            diagnostics.AddRange(parsed.Value.Diagnostics);
        }

        var resourceNodes = BuildResourceNodes(operations);
        var edges = BuildEdges(operations);
        return ResultFactory.Success(new RabbitMqTopologyFacts(
            resourceNodes,
            edges,
            [.. diagnostics
                .OrderBy(static diagnostic => diagnostic.RepositoryRelativePath, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)]));
    }

    private static async Task<Result<ParsedRabbitMqFile>> ParseAsync(
        string repositoryRoot,
        SourceFile file,
        Dictionary<string, GraphNodeFact[]> typesBySimpleName,
        IReadOnlyDictionary<string, GraphNodeFact> fileNodesByPath,
        CancellationToken cancellationToken)
    {
        var path = ResolveRepositoryFile(repositoryRoot, file.RepositoryRelativePath);
        if (!path.IsSuccess)
        {
            return ResultFactory.Failure<ParsedRabbitMqFile>(path.Problem!);
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<ParsedRabbitMqFile>(
                Problem.Storage($"Archy could not read RabbitMQ provider source '{file.RepositoryRelativePath}': {exception.Message}"));
        }

        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), file.ContentHash, StringComparison.Ordinal))
        {
            return ResultFactory.Failure<ParsedRabbitMqFile>(
                Problem.Conflict($"C# source '{file.RepositoryRelativePath}' changed after source inventory. Run analysis again."));
        }

        var tree = CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(bytes), path: file.RepositoryRelativePath, cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken);
        var operations = new List<RabbitMqOperation>();
        var diagnostics = new List<RabbitMqTopologyDiagnostic>();
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!TryGetOperationKind(invocation, out var kind))
            {
                continue;
            }

            if (!TryGetSourceNode(invocation, file.RepositoryRelativePath, typesBySimpleName, fileNodesByPath, out var source))
            {
                continue;
            }

            var position = tree.GetLineSpan(invocation.Span, cancellationToken).StartLinePosition;
            if (!TryBuildOperation(kind, invocation, source.StableId, out var operationValues))
            {
                diagnostics.Add(new RabbitMqTopologyDiagnostic(
                    file.RepositoryRelativePath,
                    position.Line + 1,
                    position.Character + 1,
                    "dynamic_rabbitmq_route",
                    $"Archy found {kind} with a non-static exchange, queue, or routing key. It is intentionally not converted into a RabbitMQ topology edge."));
                continue;
            }

            operations.Add(new RabbitMqOperation(
                kind,
                operationValues.Exchange,
                operationValues.Queue,
                operationValues.RoutingKey,
                source.StableId,
                file.RepositoryRelativePath,
                position.Line + 1,
                position.Character + 1));
        }

        return ResultFactory.Success(new ParsedRabbitMqFile(operations, diagnostics));
    }

    private static IReadOnlyList<GraphNodeFact> BuildResourceNodes(IReadOnlyList<RabbitMqOperation> operations)
    {
        var nodes = new Dictionary<string, GraphNodeFact>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            if (operation.Exchange is not null)
            {
                var stableId = ExchangeStableId(operation.Exchange);
                nodes.TryAdd(stableId, CreateResourceNode(
                    stableId,
                    "message_exchange",
                    ExchangeDisplayName(operation.Exchange),
                    operation));
            }

            if (operation.Queue is not null)
            {
                var stableId = QueueStableId(operation.Queue);
                nodes.TryAdd(stableId, CreateResourceNode(
                    stableId,
                    "message_queue",
                    operation.Queue,
                    operation));
            }
        }

        return [.. nodes.Values.OrderBy(static node => node.StableId, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<GraphEdgeFact> BuildEdges(IReadOnlyList<RabbitMqOperation> operations)
    {
        var edges = new Dictionary<string, GraphEdgeFact>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            switch (operation.Kind)
            {
                case RabbitMqOperationKind.Publish:
                    AddEdge(
                        edges,
                        operation.SourceStableId,
                        ExchangeStableId(operation.Exchange!),
                        "rabbitmq_publish",
                        operation.RoutingKey!,
                        operation,
                        consumerStableId: null);
                    break;
                case RabbitMqOperationKind.QueueBind:
                    AddEdge(
                        edges,
                        ExchangeStableId(operation.Exchange!),
                        QueueStableId(operation.Queue!),
                        "rabbitmq_route",
                        operation.RoutingKey!,
                        operation,
                        consumerStableId: null);
                    break;
                case RabbitMqOperationKind.Consume:
                    AddEdge(
                        edges,
                        QueueStableId(operation.Queue!),
                        operation.SourceStableId,
                        "rabbitmq_consume",
                        operation.Queue!,
                        operation,
                        consumerStableId: operation.SourceStableId);
                    break;
            }
        }

        return [.. edges.Values.OrderBy(static edge => edge.EdgeId, StringComparer.Ordinal)];
    }

    private static void AddEdge(
        Dictionary<string, GraphEdgeFact> edges,
        string sourceStableId,
        string targetStableId,
        string edgeKind,
        string joinKey,
        RabbitMqOperation operation,
        string? consumerStableId)
    {
        var edgeId = EdgeId(sourceStableId, targetStableId, edgeKind, joinKey);
        edges.TryAdd(edgeId, new GraphEdgeFact(
            edgeId,
            sourceStableId,
            targetStableId,
            edgeKind,
            joinKey,
            Provider,
            Confidence,
            EvidenceJson: JsonSerializer.Serialize(
                new RabbitMqOperationEvidence(
                    operation.Kind.ToString().ToLowerInvariant(),
                    operation.Exchange,
                    operation.Queue,
                    operation.RoutingKey,
                    operation.Kind == RabbitMqOperationKind.Publish ? operation.SourceStableId : null,
                    consumerStableId,
                    operation.RepositoryRelativePath,
                    operation.Line,
                    operation.Column),
                RabbitMqTopologyJsonContext.Default.RabbitMqOperationEvidence)));
    }

    private static GraphNodeFact CreateResourceNode(
        string stableId,
        string nodeKind,
        string displayName,
        RabbitMqOperation operation)
    {
        var evidence = JsonSerializer.Serialize(
            new RabbitMqResourceEvidence(
                nodeKind,
                displayName,
                operation.RepositoryRelativePath,
                operation.Line,
                operation.Column),
            RabbitMqTopologyJsonContext.Default.RabbitMqResourceEvidence);
        return new GraphNodeFact(
            stableId,
            nodeKind,
            $"rabbitmq:{nodeKind}:{displayName}",
            displayName,
            FilePath: null,
            StartLine: null,
            EndLine: null,
            Provider,
            Confidence,
            EvidenceJson: evidence,
            ContentHash: Hash(evidence));
    }

    private static bool TryBuildOperation(
        RabbitMqOperationKind kind,
        InvocationExpressionSyntax invocation,
        string sourceStableId,
        out RabbitMqOperationValues values)
    {
        values = default;
        return kind switch
        {
            RabbitMqOperationKind.Publish =>
                TryGetStaticStringArgument(invocation, "exchange", 0, out var exchange) &&
                TryGetStaticStringArgument(invocation, "routingKey", 1, out var routingKey) &&
                TryCreatePublishValues(exchange, routingKey, sourceStableId, out values),
            RabbitMqOperationKind.QueueBind =>
                TryGetStaticStringArgument(invocation, "queue", 0, out var queue) &&
                TryGetStaticStringArgument(invocation, "exchange", 1, out var exchange) &&
                TryGetStaticStringArgument(invocation, "routingKey", 2, out var routingKey) &&
                TryCreateQueueBindValues(exchange, queue, routingKey, sourceStableId, out values),
            RabbitMqOperationKind.Consume =>
                TryGetStaticStringArgument(invocation, "queue", 0, out var queue) &&
                TryCreateConsumeValues(queue, sourceStableId, out values),
            _ => false,
        };
    }

    private static bool TryCreatePublishValues(string exchange, string routingKey, string sourceStableId, out RabbitMqOperationValues values)
    {
        values = new RabbitMqOperationValues(exchange, Queue: null, routingKey, sourceStableId);
        return true;
    }

    private static bool TryCreateQueueBindValues(string exchange, string queue, string routingKey, string sourceStableId, out RabbitMqOperationValues values)
    {
        values = new RabbitMqOperationValues(exchange, queue, routingKey, sourceStableId);
        return true;
    }

    private static bool TryCreateConsumeValues(string queue, string sourceStableId, out RabbitMqOperationValues values)
    {
        values = new RabbitMqOperationValues(Exchange: null, queue, RoutingKey: null, sourceStableId);
        return true;
    }

    private static bool TryGetOperationKind(InvocationExpressionSyntax invocation, out RabbitMqOperationKind kind)
    {
        kind = GetInvokedMethodName(invocation.Expression) switch
        {
            "BasicPublish" => RabbitMqOperationKind.Publish,
            "QueueBind" => RabbitMqOperationKind.QueueBind,
            "BasicConsume" => RabbitMqOperationKind.Consume,
            _ => RabbitMqOperationKind.Unknown,
        };
        return kind != RabbitMqOperationKind.Unknown;
    }

    private static bool TryGetStaticStringArgument(
        InvocationExpressionSyntax invocation,
        string parameterName,
        int positionalIndex,
        out string value)
    {
        value = string.Empty;
        var argument = invocation.ArgumentList.Arguments
            .FirstOrDefault(argument => string.Equals(argument.NameColon?.Name.Identifier.ValueText, parameterName, StringComparison.Ordinal));
        if (argument == default)
        {
            argument = invocation.ArgumentList.Arguments.Count > positionalIndex
                ? invocation.ArgumentList.Arguments[positionalIndex]
                : default;
        }

        return argument != default && TryGetStaticString(argument.Expression, out value);
    }

    private static bool TryGetStaticString(ExpressionSyntax expression, out string value)
    {
        value = string.Empty;
        if (expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } literal)
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

    private static string GetInvokedMethodName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        _ => string.Empty,
    };

    private static Result<string> ResolveRepositoryFile(string repositoryRoot, string repositoryRelativePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var fullPath = Path.GetFullPath(Path.Combine(root, repositoryRelativePath));
        return Path.IsPathRooted(repositoryRelativePath) ||
               !fullPath.StartsWith(string.Concat(root, Path.DirectorySeparatorChar), StringComparison.Ordinal)
            ? ResultFactory.Failure<string>(Problem.Validation($"RabbitMQ provider source path '{repositoryRelativePath}' escapes the repository root."))
            : ResultFactory.Success(fullPath);
    }

    private static string GetSimpleTypeName(string fullyQualifiedName)
    {
        var lastSegment = fullyQualifiedName.Split('.').LastOrDefault() ?? string.Empty;
        var arityIndex = lastSegment.IndexOf('`', StringComparison.Ordinal);
        return arityIndex < 0 ? lastSegment : lastSegment[..arityIndex];
    }

    private static string ExchangeStableId(string exchange) => $"rabbitmq:exchange:{(exchange.Length == 0 ? "amq.default" : exchange)}";

    private static string QueueStableId(string queue) => $"rabbitmq:queue:{queue}";

    private static string ExchangeDisplayName(string exchange) => exchange.Length == 0 ? "amq.default" : exchange;

    private static string EdgeId(string sourceStableId, string targetStableId, string edgeKind, string joinKey) =>
        $"edge:{Hash($"{sourceStableId}\u001f{targetStableId}\u001f{edgeKind}\u001f{joinKey}")}";

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private enum RabbitMqOperationKind
    {
        Unknown,
        Publish,
        QueueBind,
        Consume,
    }

    private readonly record struct RabbitMqOperationValues(
        string? Exchange,
        string? Queue,
        string? RoutingKey,
        string SourceStableId);

    private sealed record RabbitMqOperation(
        RabbitMqOperationKind Kind,
        string? Exchange,
        string? Queue,
        string? RoutingKey,
        string SourceStableId,
        string RepositoryRelativePath,
        int Line,
        int Column);

    private sealed record ParsedRabbitMqFile(
        IReadOnlyList<RabbitMqOperation> Operations,
        IReadOnlyList<RabbitMqTopologyDiagnostic> Diagnostics);
}
