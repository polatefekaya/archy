using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Archy.Features.Analysis.InventorySources;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ResolveDotNetDependencyRegistrations;

public sealed class DotNetDependencyRegistrationProvider : IDotNetDependencyRegistrationProvider
{
    private const string Provider = "dotnet-di-syntax";
    private static readonly HashSet<string> RegistrationMethods = ["AddScoped", "AddSingleton", "AddTransient"];

    public async ValueTask<Result<DotNetDependencyRegistrationFacts>> ResolveAsync(
        string repositoryRoot,
        IReadOnlyList<SourceFile> files,
        IReadOnlyList<GraphNodeFact> nodes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(nodes);

        var nodeCandidates = nodes
            .Where(static node => node.NodeKind is "class" or "struct" or "interface" or "record" or "record_struct")
            .GroupBy(static node => GetSimpleTypeName(node.DisplayName), StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static node => node.StableId, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var edges = new Dictionary<string, GraphEdgeFact>(StringComparer.Ordinal);
        var diagnostics = new List<DotNetDependencyRegistrationDiagnostic>();
        var root = Path.GetFullPath(repositoryRoot);

        foreach (var file in files
                     .Where(static file => file.Language == SourceLanguage.CSharp)
                     .OrderBy(static file => file.RepositoryRelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = await ParseAsync(root, file, cancellationToken);
            if (!parsed.IsSuccess)
            {
                return ResultFactory.Failure<DotNetDependencyRegistrationFacts>(parsed.Problem!);
            }

            diagnostics.AddRange(parsed.Value.Diagnostics);
            foreach (var registration in parsed.Value.Registrations)
            {
                if (!TryResolve(nodeCandidates, registration.ServiceType, out var service) ||
                    !TryResolve(nodeCandidates, registration.ImplementationType, out var implementation))
                {
                    diagnostics.Add(new DotNetDependencyRegistrationDiagnostic(
                        file.RepositoryRelativePath,
                        registration.Line,
                        registration.Column,
                        "unresolved_di_registration",
                        $"Archy could not unambiguously resolve {registration.ServiceType} -> {registration.ImplementationType} from {registration.Method}."));
                    continue;
                }

                var joinKey = $"{registration.Method}:{registration.ServiceType}=>{registration.ImplementationType}";
                var edgeId = EdgeId(service.StableId, implementation.StableId, "di_registration", joinKey);
                edges.TryAdd(edgeId, new GraphEdgeFact(
                    edgeId,
                    service.StableId,
                    implementation.StableId,
                    "di_registration",
                    joinKey,
                    Provider,
                    Confidence: 0.8,
                    EvidenceJson: JsonSerializer.Serialize(
                        new DotNetDependencyRegistrationEvidence(
                            registration.Method,
                            registration.ServiceType,
                            registration.ImplementationType,
                            file.RepositoryRelativePath,
                            registration.Line,
                            registration.Column),
                        DotNetDependencyRegistrationJsonContext.Default.DotNetDependencyRegistrationEvidence)));
            }
        }

        return ResultFactory.Success(new DotNetDependencyRegistrationFacts(
            [.. edges.Values.OrderBy(static edge => edge.EdgeId, StringComparer.Ordinal)],
            [.. diagnostics
                .OrderBy(static diagnostic => diagnostic.RepositoryRelativePath, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)]));
    }

    private static async Task<Result<ParsedRegistrationFile>> ParseAsync(
        string repositoryRoot,
        SourceFile file,
        CancellationToken cancellationToken)
    {
        var path = ResolveRepositoryFile(repositoryRoot, file.RepositoryRelativePath);
        if (!path.IsSuccess)
        {
            return ResultFactory.Failure<ParsedRegistrationFile>(path.Problem!);
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<ParsedRegistrationFile>(
                Problem.Storage($"Archy could not read DI registration source '{file.RepositoryRelativePath}': {exception.Message}"));
        }

        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), file.ContentHash, StringComparison.Ordinal))
        {
            return ResultFactory.Failure<ParsedRegistrationFile>(
                Problem.Conflict($"C# source '{file.RepositoryRelativePath}' changed after source inventory. Run analysis again."));
        }

        var source = Encoding.UTF8.GetString(bytes);
        var tree = CSharpSyntaxTree.ParseText(source, path: file.RepositoryRelativePath, cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken);
        var registrations = new List<RegistrationSite>();
        var diagnostics = new List<DotNetDependencyRegistrationDiagnostic>();
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (TryGetRegistration(invocation, out var method, out var serviceType, out var implementationType))
            {
                var position = tree.GetLineSpan(invocation.Span, cancellationToken).StartLinePosition;
                registrations.Add(new RegistrationSite(
                    method,
                    serviceType,
                    implementationType,
                    position.Line + 1,
                    position.Character + 1));
            }

            if (TryGetUnsupportedRegistrationPattern(invocation, out var pattern))
            {
                var position = tree.GetLineSpan(invocation.Span, cancellationToken).StartLinePosition;
                diagnostics.Add(new DotNetDependencyRegistrationDiagnostic(
                    file.RepositoryRelativePath,
                    position.Line + 1,
                    position.Character + 1,
                    "unsupported_di_registration_pattern",
                    $"Archy detected '{pattern}', an assembly-scanning or reflection-based DI registration pattern. It is intentionally not converted into guessed registration edges."));
            }
        }

        return ResultFactory.Success(new ParsedRegistrationFile(registrations, diagnostics));
    }

    private static bool TryGetRegistration(
        InvocationExpressionSyntax invocation,
        out string method,
        out string serviceType,
        out string implementationType)
    {
        method = string.Empty;
        serviceType = string.Empty;
        implementationType = string.Empty;
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName } ||
            !RegistrationMethods.Contains(genericName.Identifier.ValueText) ||
            genericName.TypeArgumentList.Arguments.Count != 2)
        {
            return false;
        }

        var service = GetSimpleTypeName(genericName.TypeArgumentList.Arguments[0]);
        var implementation = GetSimpleTypeName(genericName.TypeArgumentList.Arguments[1]);
        if (service.Length == 0 || implementation.Length == 0)
        {
            return false;
        }

        method = genericName.Identifier.ValueText;
        serviceType = service;
        implementationType = implementation;
        return true;
    }

    private static bool TryGetUnsupportedRegistrationPattern(
        InvocationExpressionSyntax invocation,
        out string pattern)
    {
        pattern = GetInvokedMethodName(invocation.Expression);
        if (string.Equals(pattern, "RegisterAssemblyTypes", StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(pattern, "Scan", StringComparison.Ordinal) ||
            !invocation.ArgumentList.Arguments
                .Select(static argument => argument.Expression)
                .OfType<LambdaExpressionSyntax>()
                .SelectMany(static lambda => lambda.DescendantNodes().OfType<InvocationExpressionSyntax>())
                .Select(static nested => GetInvokedMethodName(nested.Expression))
                .Any(static nestedName => nestedName is "FromAssemblyOf" or "FromAssembliesOf" or "FromCallingAssembly" or "FromExecutingAssembly" or "AddClasses" or "AsImplementedInterfaces"))
        {
            pattern = string.Empty;
            return false;
        }

        return true;
    }

    private static string GetInvokedMethodName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        _ => string.Empty,
    };

    private static bool TryResolve(
        Dictionary<string, GraphNodeFact[]> candidates,
        string typeName,
        out GraphNodeFact node)
    {
        node = null!;
        if (!candidates.TryGetValue(typeName, out var matches) || matches.Length != 1)
        {
            return false;
        }

        node = matches[0];
        return true;
    }

    private static Result<string> ResolveRepositoryFile(string repositoryRoot, string repositoryRelativePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var fullPath = Path.GetFullPath(Path.Combine(root, repositoryRelativePath));
        if (Path.IsPathRooted(repositoryRelativePath) ||
            !fullPath.StartsWith(string.Concat(root, Path.DirectorySeparatorChar), StringComparison.Ordinal))
        {
            return ResultFactory.Failure<string>(
                Problem.Validation($"DI registration source path '{repositoryRelativePath}' escapes the repository root."));
        }

        return ResultFactory.Success(fullPath);
    }

    private static string GetSimpleTypeName(GraphNodeFact node) => GetSimpleTypeName(node.DisplayName);

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

    private sealed record RegistrationSite(
        string Method,
        string ServiceType,
        string ImplementationType,
        int Line,
        int Column);

    private sealed record ParsedRegistrationFile(
        IReadOnlyList<RegistrationSite> Registrations,
        IReadOnlyList<DotNetDependencyRegistrationDiagnostic> Diagnostics);
}
