using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Archy.Features.Analysis.InventorySources;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ExtractCSharpSyntaxFacts;

public sealed class CSharpSyntaxFactExtractor : ICSharpSyntaxFactExtractor
{
    private const string Provider = "csharp-syntax";

    public async ValueTask<Result<CSharpSyntaxFactBatch>> ExtractAsync(
        string repositoryRoot,
        IReadOnlyList<SourceFile> files,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(files);

        var root = Path.GetFullPath(repositoryRoot);
        var nodes = new Dictionary<string, GraphNodeFact>(StringComparer.Ordinal);
        var edges = new Dictionary<string, GraphEdgeFact>(StringComparer.Ordinal);
        var symbols = new Dictionary<string, GraphSymbolFact>(StringComparer.Ordinal);
        var fingerprints = new Dictionary<string, InterfaceFingerprintFact>(StringComparer.Ordinal);
        var diagnostics = new List<CSharpSyntaxDiagnostic>();
        var isComplete = true;

        foreach (var file in files
                     .Where(static file => file.Language == SourceLanguage.CSharp)
                     .OrderBy(static file => file.RepositoryRelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = await ParseFileAsync(root, file, cancellationToken);
            if (!parsed.IsSuccess)
            {
                return ResultFactory.Failure<CSharpSyntaxFactBatch>(parsed.Problem!);
            }

            isComplete &= parsed.Value.IsComplete;
            diagnostics.AddRange(parsed.Value.Diagnostics);
            foreach (var node in parsed.Value.Nodes)
            {
                if (nodes.TryGetValue(node.StableId, out var existing) && !Equals(existing, node))
                {
                    return ResultFactory.Failure<CSharpSyntaxFactBatch>(
                        Problem.Validation($"C# syntax facts produced conflicting node identity '{node.StableId}'."));
                }

                nodes.TryAdd(node.StableId, node);
            }

            foreach (var edge in parsed.Value.Edges)
            {
                edges.Add(edge.EdgeId, edge);
            }

            foreach (var symbol in parsed.Value.Symbols)
            {
                symbols.Add(symbol.SymbolId, symbol);
            }

            foreach (var fingerprint in parsed.Value.InterfaceFingerprints)
            {
                fingerprints.Add(FingerprintIdentity(fingerprint), fingerprint);
            }
        }

        return ResultFactory.Success(new CSharpSyntaxFactBatch(
            isComplete,
            [.. nodes.Values.OrderBy(static node => node.StableId, StringComparer.Ordinal)],
            [.. edges.Values.OrderBy(static edge => edge.EdgeId, StringComparer.Ordinal)],
            [.. symbols.Values.OrderBy(static symbol => symbol.SymbolId, StringComparer.Ordinal)],
            [.. fingerprints.Values
                .OrderBy(static fingerprint => fingerprint.SymbolId, StringComparer.Ordinal)
                .ThenBy(static fingerprint => fingerprint.FingerprintKind, StringComparer.Ordinal)],
            [.. diagnostics
                .OrderBy(static diagnostic => diagnostic.RepositoryRelativePath, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)]));
    }

    private static async Task<Result<ParsedCSharpFile>> ParseFileAsync(
        string repositoryRoot,
        SourceFile file,
        CancellationToken cancellationToken)
    {
        var fullPath = ResolveRepositoryFile(repositoryRoot, file.RepositoryRelativePath);
        if (!fullPath.IsSuccess)
        {
            return ResultFactory.Failure<ParsedCSharpFile>(fullPath.Problem!);
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(fullPath.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<ParsedCSharpFile>(
                Problem.Storage($"Archy could not read C# source '{file.RepositoryRelativePath}': {exception.Message}"));
        }

        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), file.ContentHash, StringComparison.Ordinal))
        {
            return ResultFactory.Failure<ParsedCSharpFile>(
                Problem.Conflict($"C# source '{file.RepositoryRelativePath}' changed after source inventory. Run analysis again."));
        }

        string source;
        try
        {
            using var textStream = new MemoryStream(bytes, writable: false);
            using var reader = new StreamReader(
                textStream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: false);
            source = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (DecoderFallbackException exception)
        {
            return ResultFactory.Failure<ParsedCSharpFile>(
                Problem.Validation($"C# source '{file.RepositoryRelativePath}' has an unsupported text encoding: {exception.Message}"));
        }

        var tree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
            file.RepositoryRelativePath,
            Encoding.UTF8,
            cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken);
        var nodes = new Dictionary<string, GraphNodeFact>(StringComparer.Ordinal);
        var edges = new Dictionary<string, GraphEdgeFact>(StringComparer.Ordinal);
        var symbols = new Dictionary<string, GraphSymbolFact>(StringComparer.Ordinal);
        var fingerprints = new Dictionary<string, InterfaceFingerprintFact>(StringComparer.Ordinal);
        var diagnostics = tree.GetDiagnostics(cancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => ToDiagnostic(file.RepositoryRelativePath, diagnostic))
            .ToArray();

        var fileNode = CreateFileNode(file);
        nodes.Add(fileNode.StableId, fileNode);
        foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            AddUsingFacts(file, tree, fileNode, usingDirective, nodes, edges);
        }

        foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            AddTypeFacts(file, tree, fileNode, declaration, nodes, edges, symbols, fingerprints);
        }

        return ResultFactory.Success(new ParsedCSharpFile(
            diagnostics.Length == 0,
            [.. nodes.Values],
            [.. edges.Values],
            [.. symbols.Values],
            [.. fingerprints.Values],
            diagnostics));
    }

    private static Result<string> ResolveRepositoryFile(string repositoryRoot, string repositoryRelativePath)
    {
        if (string.IsNullOrWhiteSpace(repositoryRelativePath))
        {
            return ResultFactory.Failure<string>(Problem.Validation("C# source paths must be repository-relative."));
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var fullPath = Path.GetFullPath(Path.Combine(root, repositoryRelativePath));
        var rootPrefix = string.Concat(root, Path.DirectorySeparatorChar);
        if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal) ||
            Path.IsPathRooted(repositoryRelativePath))
        {
            return ResultFactory.Failure<string>(
                Problem.Validation($"C# source path '{repositoryRelativePath}' escapes the repository root."));
        }

        return ResultFactory.Success(fullPath);
    }

    private static GraphNodeFact CreateFileNode(SourceFile file) => new(
        FileStableId(file.RepositoryRelativePath),
        "file",
        file.RepositoryRelativePath,
        file.RepositoryRelativePath,
        file.RepositoryRelativePath,
        StartLine: null,
        EndLine: null,
        Provider,
        Confidence: 1,
        EvidenceJson: SerializeEvidence("file", file.RepositoryRelativePath, null, null, null),
        file.ContentHash);

    private static void AddUsingFacts(
        SourceFile file,
        SyntaxTree tree,
        GraphNodeFact fileNode,
        UsingDirectiveSyntax usingDirective,
        Dictionary<string, GraphNodeFact> nodes,
        Dictionary<string, GraphEdgeFact> edges)
    {
        if (usingDirective.Name is null)
        {
            return;
        }

        var reference = NormalizeSyntax(usingDirective.Name.ToString());
        if (reference.Length == 0)
        {
            return;
        }

        var referenceStableId = $"csharp:using-reference:{reference}";
        if (!nodes.ContainsKey(referenceStableId))
        {
            nodes.Add(referenceStableId, new GraphNodeFact(
                referenceStableId,
                "unresolved_reference",
                $"csharp:using:{reference}",
                reference,
                FilePath: null,
                StartLine: null,
                EndLine: null,
                Provider,
                Confidence: 0.65,
                EvidenceJson: SerializeEvidence("using_reference", "<virtual>", null, null, reference),
                ContentHash: Hash(referenceStableId)));
        }

        var range = ToLineRange(tree, usingDirective);
        var kind = usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
            ? "using_static"
            : "using";
        var joinKey = usingDirective.Alias is null
            ? reference
            : $"{usingDirective.Alias.Name.Identifier.ValueText}={reference}";
        var edgeId = EdgeId(fileNode.StableId, referenceStableId, kind, joinKey);
        edges.TryAdd(edgeId, new GraphEdgeFact(
            edgeId,
            fileNode.StableId,
            referenceStableId,
            kind,
            joinKey,
            Provider,
            Confidence: 0.65,
            EvidenceJson: SerializeEvidence(kind, file.RepositoryRelativePath, range.StartLine, range.EndLine, joinKey)));
    }

    private static void AddTypeFacts(
        SourceFile file,
        SyntaxTree tree,
        GraphNodeFact fileNode,
        BaseTypeDeclarationSyntax declaration,
        Dictionary<string, GraphNodeFact> nodes,
        Dictionary<string, GraphEdgeFact> edges,
        Dictionary<string, GraphSymbolFact> symbols,
        Dictionary<string, InterfaceFingerprintFact> fingerprints)
    {
        var qualifiedName = GetQualifiedName(declaration);
        var typeStableId = $"csharp:type:{file.RepositoryRelativePath}:{qualifiedName}";
        var range = ToLineRange(tree, declaration);
        if (!nodes.ContainsKey(typeStableId))
        {
            nodes.Add(typeStableId, new GraphNodeFact(
                typeStableId,
                DeclarationKind(declaration),
                $"csharp:type:{qualifiedName}",
                qualifiedName,
                file.RepositoryRelativePath,
                range.StartLine,
                range.EndLine,
                Provider,
                Confidence: 1,
                EvidenceJson: SerializeEvidence("type_declaration", file.RepositoryRelativePath, range.StartLine, range.EndLine, qualifiedName),
                file.ContentHash));
        }

        var declarationEdgeId = EdgeId(fileNode.StableId, typeStableId, "declares", qualifiedName);
        edges.TryAdd(declarationEdgeId, new GraphEdgeFact(
            declarationEdgeId,
            fileNode.StableId,
            typeStableId,
            "declares",
            qualifiedName,
            Provider,
            Confidence: 1,
            EvidenceJson: SerializeEvidence("declares", file.RepositoryRelativePath, range.StartLine, range.EndLine, qualifiedName)));

        var symbolId = $"csharp:symbol:{file.RepositoryRelativePath}:{qualifiedName}";
        var visibility = GetVisibility(declaration.Modifiers, declaration is InterfaceDeclarationSyntax);
        var normalizedSignature = $"{DeclarationKind(declaration)}:{qualifiedName}";
        symbols.TryAdd(symbolId, new GraphSymbolFact(
            symbolId,
            typeStableId,
            qualifiedName,
            visibility,
            normalizedSignature,
            ParameterMetadataJson: "[]",
            ReturnMetadataJson: "{}",
            SignatureHash: Hash(normalizedSignature)));

        if (!ShouldFingerprint(declaration, visibility))
        {
            return;
        }

        var normalizedMembers = ExtractPublicSurfaceMembers(declaration, declaration is InterfaceDeclarationSyntax);
        var membersJson = JsonSerializer.Serialize(
            normalizedMembers,
            CSharpSyntaxFactJsonContext.Default.StringArray);
        var fingerprint = new InterfaceFingerprintFact(
            symbolId,
            "public-surface-v1",
            Hash(membersJson),
            membersJson);
        fingerprints.TryAdd(FingerprintIdentity(fingerprint), fingerprint);
    }

    private static CSharpSyntaxDiagnostic ToDiagnostic(string repositoryRelativePath, Diagnostic diagnostic)
    {
        var position = diagnostic.Location.GetLineSpan().StartLinePosition;
        return new CSharpSyntaxDiagnostic(
            repositoryRelativePath,
            diagnostic.Id,
            diagnostic.Severity.ToString().ToLowerInvariant(),
            position.Line + 1,
            position.Character + 1,
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    private static (int StartLine, int EndLine) ToLineRange(SyntaxTree tree, SyntaxNode node)
    {
        var lineSpan = tree.GetLineSpan(node.Span);
        return (lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1);
    }

    private static string GetQualifiedName(BaseTypeDeclarationSyntax declaration)
    {
        var namespaceParts = declaration.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(static @namespace => NormalizeSyntax(@namespace.Name.ToString()));
        var containingTypeParts = declaration.Ancestors()
            .OfType<BaseTypeDeclarationSyntax>()
            .Reverse()
            .Select(GetTypeName);
        return string.Join('.', namespaceParts.Concat(containingTypeParts).Append(GetTypeName(declaration)));
    }

    private static string GetTypeName(BaseTypeDeclarationSyntax declaration) => declaration switch
    {
        TypeDeclarationSyntax type when type.TypeParameterList is { Parameters.Count: > 0 } parameters =>
            $"{type.Identifier.ValueText}`{parameters.Parameters.Count}",
        _ => declaration.Identifier.ValueText,
    };

    private static string DeclarationKind(BaseTypeDeclarationSyntax declaration) => declaration.Kind() switch
    {
        SyntaxKind.ClassDeclaration => "class",
        SyntaxKind.StructDeclaration => "struct",
        SyntaxKind.InterfaceDeclaration => "interface",
        SyntaxKind.EnumDeclaration => "enum",
        SyntaxKind.RecordDeclaration => "record",
        SyntaxKind.RecordStructDeclaration => "record_struct",
        _ => "type",
    };

    private static string GetVisibility(SyntaxTokenList modifiers, bool isInterface)
    {
        if (modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PublicKeyword)) || isInterface)
        {
            return "public";
        }

        var isProtected = modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.ProtectedKeyword));
        if (isProtected && modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.InternalKeyword)))
        {
            return "protected_internal";
        }

        if (isProtected)
        {
            return "protected";
        }

        return modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PrivateKeyword))
            ? "private"
            : "internal";
    }

    private static bool ShouldFingerprint(BaseTypeDeclarationSyntax declaration, string visibility) =>
        declaration is InterfaceDeclarationSyntax ||
        visibility is "public" or "protected" or "protected_internal";

    private static string[] ExtractPublicSurfaceMembers(BaseTypeDeclarationSyntax declaration, bool isInterface)
    {
        var declaredMembers = declaration is TypeDeclarationSyntax typeDeclaration
            ? typeDeclaration.Members
            : [];
        var members = declaredMembers
            .Where(member => isInterface || IsPublicSurfaceMember(member))
            .Select(NormalizeMember)
            .Where(static member => member is not null)
            .Select(static member => member!)
            .Concat(declaration.BaseList?.Types.Select(baseType => $"base:{NormalizeSyntax(baseType.Type.ToString())}") ?? [])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static member => member, StringComparer.Ordinal)
            .ToArray();
        return members;
    }

    private static bool IsPublicSurfaceMember(MemberDeclarationSyntax member) => member switch
    {
        BaseMethodDeclarationSyntax method => HasPublicOrProtectedModifier(method.Modifiers),
        EventDeclarationSyntax @event => HasPublicOrProtectedModifier(@event.Modifiers),
        BasePropertyDeclarationSyntax property => HasPublicOrProtectedModifier(property.Modifiers),
        BaseFieldDeclarationSyntax field => HasPublicOrProtectedModifier(field.Modifiers),
        _ => false,
    };

    private static bool HasPublicOrProtectedModifier(SyntaxTokenList modifiers) =>
        modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PublicKeyword) || modifier.IsKind(SyntaxKind.ProtectedKeyword));

    private static string? NormalizeMember(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax method => $"method:{NormalizeSyntax(method.ReturnType.ToString())}:{method.Identifier.ValueText}{NormalizeTypeParameterList(method.TypeParameterList)}({NormalizeParameters(method.ParameterList.Parameters)})",
        ConstructorDeclarationSyntax constructor => $"constructor:{constructor.Identifier.ValueText}({NormalizeParameters(constructor.ParameterList.Parameters)})",
        DestructorDeclarationSyntax destructor => $"destructor:{destructor.Identifier.ValueText}()",
        OperatorDeclarationSyntax @operator => $"operator:{NormalizeSyntax(@operator.ReturnType.ToString())}:{@operator.OperatorToken.ValueText}({NormalizeParameters(@operator.ParameterList.Parameters)})",
        ConversionOperatorDeclarationSyntax conversion => $"conversion:{conversion.ImplicitOrExplicitKeyword.ValueText}:{NormalizeSyntax(conversion.Type.ToString())}({NormalizeParameters(conversion.ParameterList.Parameters)})",
        PropertyDeclarationSyntax property => $"property:{NormalizeSyntax(property.Type.ToString())}:{property.Identifier.ValueText}",
        IndexerDeclarationSyntax indexer => $"indexer:{NormalizeSyntax(indexer.Type.ToString())}({NormalizeParameters(indexer.ParameterList.Parameters)})",
        EventDeclarationSyntax @event => $"event:{NormalizeSyntax(@event.Type.ToString())}:{@event.Identifier.ValueText}",
        EventFieldDeclarationSyntax eventField => $"event_field:{NormalizeSyntax(eventField.Declaration.Type.ToString())}:{string.Join(',', eventField.Declaration.Variables.Select(static variable => variable.Identifier.ValueText))}",
        FieldDeclarationSyntax field => $"field:{NormalizeSyntax(field.Declaration.Type.ToString())}:{string.Join(',', field.Declaration.Variables.Select(static variable => variable.Identifier.ValueText))}",
        _ => null,
    };

    private static string NormalizeParameters(SeparatedSyntaxList<ParameterSyntax> parameters) =>
        string.Join(',', parameters.Select(static parameter =>
            $"{string.Join(' ', parameter.Modifiers.Select(static modifier => modifier.ValueText))}:{NormalizeSyntax(parameter.Type?.ToString() ?? "?")}"));

    private static string NormalizeTypeParameterList(TypeParameterListSyntax? parameters) =>
        parameters is null
            ? string.Empty
            : $"`{parameters.Parameters.Count}";

    private static string NormalizeSyntax(string value) =>
        string.Concat(value.Where(static character => !char.IsWhiteSpace(character)));

    private static string FileStableId(string repositoryRelativePath) => $"file:{repositoryRelativePath}";

    private static string EdgeId(string sourceStableId, string targetStableId, string edgeKind, string joinKey) =>
        $"edge:{Hash($"{sourceStableId}\u001f{targetStableId}\u001f{edgeKind}\u001f{joinKey}")}";

    private static string FingerprintIdentity(InterfaceFingerprintFact fingerprint) =>
        $"{fingerprint.SymbolId}\u001f{fingerprint.FingerprintKind}";

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string SerializeEvidence(
        string kind,
        string repositoryRelativePath,
        int? startLine,
        int? endLine,
        string? detail) =>
        JsonSerializer.Serialize(
            new CSharpSyntaxEvidence(kind, repositoryRelativePath, startLine, endLine, detail),
            CSharpSyntaxFactJsonContext.Default.CSharpSyntaxEvidence);

    private sealed record ParsedCSharpFile(
        bool IsComplete,
        IReadOnlyList<GraphNodeFact> Nodes,
        IReadOnlyList<GraphEdgeFact> Edges,
        IReadOnlyList<GraphSymbolFact> Symbols,
        IReadOnlyList<InterfaceFingerprintFact> InterfaceFingerprints,
        IReadOnlyList<CSharpSyntaxDiagnostic> Diagnostics);
}
