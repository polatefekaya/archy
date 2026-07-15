using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Archy.Features.Analysis.InventorySources;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ResolveDotNetDependencyConsumptions;

public sealed class DotNetDependencyConsumptionProvider : IDotNetDependencyConsumptionProvider
{
    private const string Provider = "dotnet-di-consumption-syntax";

    public async ValueTask<Result<DotNetDependencyConsumptionFacts>> ResolveAsync(
        string repositoryRoot,
        IReadOnlyList<SourceFile> files,
        IReadOnlyList<GraphNodeFact> nodes,
        IReadOnlyList<GraphEdgeFact> dependencyRegistrationEdges,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(dependencyRegistrationEdges);

        var typesBySimpleName = nodes
            .Where(static node => node.NodeKind is "class" or "struct" or "interface" or "record" or "record_struct")
            .GroupBy(static node => GetSimpleTypeName(node.DisplayName), StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static node => node.StableId, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var registrationsByService = dependencyRegistrationEdges
            .Where(static edge => edge.EdgeKind == "di_registration")
            .GroupBy(static edge => edge.SourceStableId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static edge => edge.TargetStableId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static stableId => stableId, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        var edges = new Dictionary<string, GraphEdgeFact>(StringComparer.Ordinal);
        var diagnostics = new List<DotNetDependencyConsumptionDiagnostic>();
        var repositoryFullPath = Path.GetFullPath(repositoryRoot);

        foreach (var file in files
                     .Where(static file => file.Language == SourceLanguage.CSharp)
                     .OrderBy(static file => file.RepositoryRelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = await ParseAsync(repositoryFullPath, file, typesBySimpleName, cancellationToken);
            if (!parsed.IsSuccess)
            {
                return ResultFactory.Failure<DotNetDependencyConsumptionFacts>(parsed.Problem!);
            }

            foreach (var site in parsed.Value)
            {
                if (!TryResolveType(typesBySimpleName, site.ServiceType, repositoryRelativePath: null, out var service))
                {
                    diagnostics.Add(new DotNetDependencyConsumptionDiagnostic(
                        site.RepositoryRelativePath,
                        site.Line,
                        site.Column,
                        "unresolved_constructor_dependency",
                        $"Archy could not unambiguously resolve constructor parameter '{site.ParameterName}' of type '{site.ServiceType}' in '{site.ConsumerType}'."));
                    continue;
                }

                if (!registrationsByService.TryGetValue(service.StableId, out var implementations))
                {
                    diagnostics.Add(new DotNetDependencyConsumptionDiagnostic(
                        site.RepositoryRelativePath,
                        site.Line,
                        site.Column,
                        "unregistered_constructor_dependency",
                        $"Archy found constructor parameter '{site.ParameterName}' of type '{site.ServiceType}' in '{site.ConsumerType}', but no supported explicit registration exists."));
                    continue;
                }

                if (implementations.Length > 1)
                {
                    diagnostics.Add(new DotNetDependencyConsumptionDiagnostic(
                        site.RepositoryRelativePath,
                        site.Line,
                        site.Column,
                        "ambiguous_di_registration",
                        $"Archy found {implementations.Length} explicit registrations for constructor dependency '{site.ServiceType}' in '{site.ConsumerType}'. The consumer-to-service fact is retained without selecting an implementation."));
                }

                var edgeId = EdgeId(site.ConsumerStableId, service.StableId, "di_consumes", site.ServiceType);
                edges.TryAdd(edgeId, new GraphEdgeFact(
                    edgeId,
                    site.ConsumerStableId,
                    service.StableId,
                    "di_consumes",
                    site.ServiceType,
                    Provider,
                    Confidence: 0.8,
                    EvidenceJson: JsonSerializer.Serialize(
                        new DotNetDependencyConsumptionEvidence(
                            site.ConsumerType,
                            site.ServiceType,
                            site.ParameterName,
                            site.RepositoryRelativePath,
                            site.Line,
                            site.Column,
                            implementations.Length),
                        DotNetDependencyConsumptionJsonContext.Default.DotNetDependencyConsumptionEvidence)));
            }
        }

        return ResultFactory.Success(new DotNetDependencyConsumptionFacts(
            [.. edges.Values.OrderBy(static edge => edge.EdgeId, StringComparer.Ordinal)],
            [.. diagnostics
                .OrderBy(static diagnostic => diagnostic.RepositoryRelativePath, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)]));
    }

    private static async Task<Result<IReadOnlyList<ConstructorDependencySite>>> ParseAsync(
        string repositoryRoot,
        SourceFile file,
        Dictionary<string, GraphNodeFact[]> typesBySimpleName,
        CancellationToken cancellationToken)
    {
        var path = ResolveRepositoryFile(repositoryRoot, file.RepositoryRelativePath);
        if (!path.IsSuccess)
        {
            return ResultFactory.Failure<IReadOnlyList<ConstructorDependencySite>>(path.Problem!);
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<IReadOnlyList<ConstructorDependencySite>>(
                Problem.Storage($"Archy could not read DI consumption source '{file.RepositoryRelativePath}': {exception.Message}"));
        }

        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), file.ContentHash, StringComparison.Ordinal))
        {
            return ResultFactory.Failure<IReadOnlyList<ConstructorDependencySite>>(
                Problem.Conflict($"C# source '{file.RepositoryRelativePath}' changed after source inventory. Run analysis again."));
        }

        var tree = CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(bytes), path: file.RepositoryRelativePath, cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken);
        var dependencies = new List<ConstructorDependencySite>();
        foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (!TryResolveType(typesBySimpleName, declaration.Identifier.ValueText, file.RepositoryRelativePath, out var consumer))
            {
                continue;
            }

            foreach (var parameter in GetConstructorParameters(declaration))
            {
                var serviceType = parameter.Type is null ? string.Empty : GetSimpleTypeName(parameter.Type);
                if (serviceType.Length == 0)
                {
                    continue;
                }

                var position = tree.GetLineSpan(parameter.Span, cancellationToken).StartLinePosition;
                dependencies.Add(new ConstructorDependencySite(
                    consumer.StableId,
                    consumer.DisplayName,
                    serviceType,
                    parameter.Identifier.ValueText,
                    file.RepositoryRelativePath,
                    position.Line + 1,
                    position.Character + 1));
            }
        }

        return ResultFactory.Success<IReadOnlyList<ConstructorDependencySite>>([.. dependencies]);
    }

    private static IEnumerable<ParameterSyntax> GetConstructorParameters(TypeDeclarationSyntax declaration)
    {
        foreach (var constructor in declaration.Members.OfType<ConstructorDeclarationSyntax>())
        {
            foreach (var parameter in constructor.ParameterList.Parameters)
            {
                yield return parameter;
            }
        }

        var primaryParameterList = declaration.ChildNodes().OfType<ParameterListSyntax>().FirstOrDefault();
        if (primaryParameterList is null)
        {
            yield break;
        }

        foreach (var parameter in primaryParameterList.Parameters)
        {
            yield return parameter;
        }
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
            ? ResultFactory.Failure<string>(Problem.Validation($"DI consumption source path '{repositoryRelativePath}' escapes the repository root."))
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

    private sealed record ConstructorDependencySite(
        string ConsumerStableId,
        string ConsumerType,
        string ServiceType,
        string ParameterName,
        string RepositoryRelativePath,
        int Line,
        int Column);
}
