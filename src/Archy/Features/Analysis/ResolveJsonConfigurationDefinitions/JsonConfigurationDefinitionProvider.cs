using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Archy.Features.Analysis.ConfigurationKeys;
using Archy.Features.Analysis.InventorySources;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ResolveJsonConfigurationDefinitions;

public sealed class JsonConfigurationDefinitionProvider : IJsonConfigurationDefinitionProvider
{
    private const string Provider = "json-configuration-definition";

    public async ValueTask<Result<JsonConfigurationDefinitionFacts>> ResolveAsync(
        string repositoryRoot,
        IReadOnlyList<SourceFile> files,
        IReadOnlyList<GraphNodeFact> knownConfigurationKeys,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(knownConfigurationKeys);

        var knownKeys = knownConfigurationKeys
            .Where(static node => node.NodeKind == "configuration_key")
            .ToDictionary(static node => node.StableId, StringComparer.Ordinal);
        var nodes = new Dictionary<string, GraphNodeFact>(StringComparer.Ordinal);
        var definitions = new List<JsonConfigurationDefinitionSite>();
        var diagnostics = new List<JsonConfigurationDefinitionDiagnostic>();
        var repositoryFullPath = Path.GetFullPath(repositoryRoot);

        foreach (var file in files
                     .Where(IsSupportedConfigurationFile)
                     .OrderBy(static file => file.RepositoryRelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = await ReadAndParseAsync(repositoryFullPath, file, cancellationToken);
            if (!parsed.IsSuccess)
            {
                return ResultFactory.Failure<JsonConfigurationDefinitionFacts>(parsed.Problem!);
            }

            nodes.Add(parsed.Value.FileNode.StableId, parsed.Value.FileNode);
            definitions.AddRange(parsed.Value.Definitions);
            diagnostics.AddRange(parsed.Value.Diagnostics);
        }

        var ambiguousDefinitions = definitions
            .GroupBy(static definition => (definition.RepositoryRelativePath, definition.KeyPath))
            .Where(static group => group.Count() > 1)
            .ToArray();
        foreach (var duplicate in ambiguousDefinitions)
        {
            var first = duplicate.OrderBy(static definition => definition.Line).ThenBy(static definition => definition.Column).First();
            diagnostics.Add(new JsonConfigurationDefinitionDiagnostic(
                first.RepositoryRelativePath,
                first.Line,
                first.Column,
                "ambiguous_configuration_definition",
                $"Archy found {duplicate.Count()} definitions for '{first.KeyPath}' in one JSON configuration file. No definition edge is inferred for that key."));
        }

        var ambiguousIdentities = ambiguousDefinitions
            .Select(static group => group.Key)
            .ToHashSet();
        var unambiguousDefinitions = definitions
            .Where(definition => !ambiguousIdentities.Contains((definition.RepositoryRelativePath, definition.KeyPath)))
            .ToArray();
        foreach (var definitionGroup in unambiguousDefinitions
                     .GroupBy(static definition => definition.KeyPath, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var stableId = ConfigurationKeyPath.StableId(definitionGroup.Key);
            if (knownKeys.ContainsKey(stableId))
            {
                continue;
            }

            var evidenceDefinitions = definitionGroup
                .OrderBy(static definition => definition.RepositoryRelativePath, StringComparer.Ordinal)
                .ThenBy(static definition => definition.Line)
                .ThenBy(static definition => definition.Column)
                .Select(static definition => new JsonConfigurationDefinitionEvidence(
                    definition.KeyPath,
                    definition.JsonPointer,
                    definition.RepositoryRelativePath,
                    definition.Line,
                    definition.Column))
                .ToArray();
            var evidence = JsonSerializer.Serialize(
                new JsonConfigurationKeyEvidence(definitionGroup.Key, evidenceDefinitions),
                JsonConfigurationDefinitionJsonContext.Default.JsonConfigurationKeyEvidence);
            nodes.Add(stableId, new GraphNodeFact(
                stableId,
                "configuration_key",
                $"configuration:key:{definitionGroup.Key}",
                definitionGroup.Key,
                FilePath: null,
                StartLine: null,
                EndLine: null,
                Provider,
                Confidence: 1,
                EvidenceJson: evidence,
                ContentHash: Hash(evidence)));
        }

        var edges = new Dictionary<string, GraphEdgeFact>(StringComparer.Ordinal);
        foreach (var definition in unambiguousDefinitions)
        {
            var sourceStableId = ConfigurationFileStableId(definition.RepositoryRelativePath);
            var targetStableId = ConfigurationKeyPath.StableId(definition.KeyPath);
            var edgeId = EdgeId(sourceStableId, targetStableId, "configuration_defines", definition.KeyPath);
            edges.TryAdd(edgeId, new GraphEdgeFact(
                edgeId,
                sourceStableId,
                targetStableId,
                "configuration_defines",
                definition.KeyPath,
                Provider,
                Confidence: 1,
                EvidenceJson: JsonSerializer.Serialize(
                    new JsonConfigurationDefinitionEvidence(
                        definition.KeyPath,
                        definition.JsonPointer,
                        definition.RepositoryRelativePath,
                        definition.Line,
                        definition.Column),
                    JsonConfigurationDefinitionJsonContext.Default.JsonConfigurationDefinitionEvidence)));
        }

        foreach (var key in knownKeys.Values.OrderBy(static node => node.DisplayName, StringComparer.Ordinal))
        {
            if (unambiguousDefinitions.Any(definition => string.Equals(definition.KeyPath, key.DisplayName, StringComparison.Ordinal)))
            {
                continue;
            }

            diagnostics.Add(CreateMissingDefinitionDiagnostic(key));
        }

        return ResultFactory.Success(new JsonConfigurationDefinitionFacts(
            [.. nodes.Values.OrderBy(static node => node.StableId, StringComparer.Ordinal)],
            [.. edges.Values.OrderBy(static edge => edge.EdgeId, StringComparer.Ordinal)],
            [.. diagnostics
                .OrderBy(static diagnostic => diagnostic.RepositoryRelativePath, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)]));
    }

    private static async Task<Result<ParsedJsonConfigurationFile>> ReadAndParseAsync(
        string repositoryRoot,
        SourceFile file,
        CancellationToken cancellationToken)
    {
        var path = ResolveRepositoryFile(repositoryRoot, file.RepositoryRelativePath);
        if (!path.IsSuccess)
        {
            return ResultFactory.Failure<ParsedJsonConfigurationFile>(path.Problem!);
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<ParsedJsonConfigurationFile>(
                Problem.Storage($"Archy could not read JSON configuration '{file.RepositoryRelativePath}': {exception.Message}"));
        }

        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), file.ContentHash, StringComparison.Ordinal))
        {
            return ResultFactory.Failure<ParsedJsonConfigurationFile>(
                Problem.Conflict($"Configuration file '{file.RepositoryRelativePath}' changed after source inventory. Run analysis again."));
        }

        var fileNode = CreateConfigurationFileNode(file, bytes);
        try
        {
            var reader = new Utf8JsonReader(
                bytes,
                isFinalBlock: true,
                state: new JsonReaderState(new JsonReaderOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                }));
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return ResultFactory.Success(new ParsedJsonConfigurationFile(
                    fileNode,
                    [],
                    [new JsonConfigurationDefinitionDiagnostic(
                        file.RepositoryRelativePath,
                        1,
                        1,
                        "invalid_configuration_json",
                        "Archy supports JSON configuration documents whose root is an object.")]));
            }

            var definitions = new List<JsonConfigurationDefinitionSite>();
            ParseObject(ref reader, bytes, file.RepositoryRelativePath, prefix: string.Empty, pointer: string.Empty, definitions);
            if (reader.Read())
            {
                throw new JsonException("Unexpected content follows the root JSON object.");
            }

            return ResultFactory.Success(new ParsedJsonConfigurationFile(fileNode, definitions, []));
        }
        catch (JsonException exception)
        {
            return ResultFactory.Success(new ParsedJsonConfigurationFile(
                fileNode,
                [],
                [new JsonConfigurationDefinitionDiagnostic(
                    file.RepositoryRelativePath,
                    ToOneBased(exception.LineNumber),
                    ToOneBased(exception.BytePositionInLine),
                    "invalid_configuration_json",
                    $"Archy could not parse this JSON configuration document: {exception.Message}")]));
        }
    }

    private static void ParseObject(
        ref Utf8JsonReader reader,
        ReadOnlySpan<byte> bytes,
        string repositoryRelativePath,
        string prefix,
        string pointer,
        List<JsonConfigurationDefinitionSite> definitions)
    {
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected a JSON property name.");
            }

            var propertyName = reader.GetString() ?? string.Empty;
            var propertyOffset = reader.TokenStartIndex;
            if (!reader.Read())
            {
                throw new JsonException("JSON property has no value.");
            }

            if (!ConfigurationKeyPath.TryCombine(prefix, propertyName, out var keyPath))
            {
                throw new JsonException($"JSON property '{propertyName}' is not a valid configuration key segment.");
            }

            var (line, column) = LineAndColumn(bytes, propertyOffset);
            var propertyPointer = string.Concat(pointer, "/", EscapeJsonPointerSegment(propertyName));
            definitions.Add(new JsonConfigurationDefinitionSite(
                keyPath,
                propertyPointer,
                repositoryRelativePath,
                line,
                column));
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    ParseObject(ref reader, bytes, repositoryRelativePath, keyPath, propertyPointer, definitions);
                    break;
                case JsonTokenType.StartArray:
                    ParseArray(ref reader, bytes, repositoryRelativePath, keyPath, propertyPointer, definitions);
                    break;
            }
        }

        throw new JsonException("JSON object was not terminated.");
    }

    private static void ParseArray(
        ref Utf8JsonReader reader,
        ReadOnlySpan<byte> bytes,
        string repositoryRelativePath,
        string prefix,
        string pointer,
        List<JsonConfigurationDefinitionSite> definitions)
    {
        var index = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                return;
            }

            var indexString = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!ConfigurationKeyPath.TryCombine(prefix, indexString, out var keyPath))
            {
                throw new JsonException($"JSON array index '{indexString}' is not a valid configuration key segment.");
            }

            var (line, column) = LineAndColumn(bytes, reader.TokenStartIndex);
            var elementPointer = string.Concat(pointer, "/", indexString);
            definitions.Add(new JsonConfigurationDefinitionSite(
                keyPath,
                elementPointer,
                repositoryRelativePath,
                line,
                column));
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    ParseObject(ref reader, bytes, repositoryRelativePath, keyPath, elementPointer, definitions);
                    break;
                case JsonTokenType.StartArray:
                    ParseArray(ref reader, bytes, repositoryRelativePath, keyPath, elementPointer, definitions);
                    break;
            }

            index++;
        }

        throw new JsonException("JSON array was not terminated.");
    }

    private static GraphNodeFact CreateConfigurationFileNode(SourceFile file, ReadOnlySpan<byte> bytes)
    {
        var evidence = JsonSerializer.Serialize(
            new JsonConfigurationFileEvidence(file.RepositoryRelativePath, file.ContentHash),
            JsonConfigurationDefinitionJsonContext.Default.JsonConfigurationFileEvidence);
        return new GraphNodeFact(
            ConfigurationFileStableId(file.RepositoryRelativePath),
            "configuration_file",
            $"configuration:file:{file.RepositoryRelativePath}",
            file.RepositoryRelativePath,
            file.RepositoryRelativePath,
            StartLine: 1,
            EndLine: CountLines(bytes),
            Provider,
            Confidence: 1,
            EvidenceJson: evidence,
            file.ContentHash);
    }

    private static JsonConfigurationDefinitionDiagnostic CreateMissingDefinitionDiagnostic(GraphNodeFact key)
    {
        var (repositoryRelativePath, line, column, isEnvironmentOnly) = ReadKeyEvidence(key);
        var code = isEnvironmentOnly ? "environment_only_configuration_key" : "missing_configuration_definition";
        var description = isEnvironmentOnly ? "an environment-only" : "a missing JSON";
        return new JsonConfigurationDefinitionDiagnostic(
            repositoryRelativePath,
            line,
            column,
            code,
            $"Archy found {description} definition for configuration key '{key.DisplayName}'.");
    }

    private static (string RepositoryRelativePath, int Line, int Column, bool IsEnvironmentOnly) ReadKeyEvidence(GraphNodeFact key)
    {
        try
        {
            using var document = JsonDocument.Parse(key.EvidenceJson);
            if (!document.RootElement.TryGetProperty("reads", out var reads) ||
                reads.ValueKind != JsonValueKind.Array ||
                reads.GetArrayLength() == 0)
            {
                return ("<configuration>", 1, 1, false);
            }

            var first = reads[0];
            var repositoryRelativePath = first.TryGetProperty("repositoryRelativePath", out var path)
                ? path.GetString() ?? "<configuration>"
                : "<configuration>";
            var line = first.TryGetProperty("line", out var lineValue) && lineValue.TryGetInt32(out var parsedLine)
                ? parsedLine
                : 1;
            var column = first.TryGetProperty("column", out var columnValue) && columnValue.TryGetInt32(out var parsedColumn)
                ? parsedColumn
                : 1;
            var isEnvironmentOnly = reads.EnumerateArray().All(static read =>
                read.TryGetProperty("accessForm", out var accessForm) &&
                string.Equals(accessForm.GetString(), "environment", StringComparison.Ordinal));
            return (repositoryRelativePath, line, column, isEnvironmentOnly);
        }
        catch (JsonException)
        {
            return ("<configuration>", 1, 1, false);
        }
    }

    private static bool IsSupportedConfigurationFile(SourceFile file)
    {
        var fileName = Path.GetFileName(file.RepositoryRelativePath);
        return fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
               fileName.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase);
    }

    private static Result<string> ResolveRepositoryFile(string repositoryRoot, string repositoryRelativePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var fullPath = Path.GetFullPath(Path.Combine(root, repositoryRelativePath));
        return Path.IsPathRooted(repositoryRelativePath) ||
               !fullPath.StartsWith(string.Concat(root, Path.DirectorySeparatorChar), StringComparison.Ordinal)
            ? ResultFactory.Failure<string>(Problem.Validation($"JSON configuration path '{repositoryRelativePath}' escapes the repository root."))
            : ResultFactory.Success(fullPath);
    }

    private static string ConfigurationFileStableId(string repositoryRelativePath) => $"configuration:file:{repositoryRelativePath}";

    private static string EdgeId(string sourceStableId, string targetStableId, string edgeKind, string joinKey) =>
        $"edge:{Hash($"{sourceStableId}\u001f{targetStableId}\u001f{edgeKind}\u001f{joinKey}")}";

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static int CountLines(ReadOnlySpan<byte> bytes)
    {
        var lines = 1;
        foreach (var value in bytes)
        {
            if (value == (byte)'\n')
            {
                lines++;
            }
        }

        return lines;
    }

    private static (int Line, int Column) LineAndColumn(ReadOnlySpan<byte> bytes, long offset)
    {
        var line = 1;
        var column = 1;
        for (var index = 0L; index < offset; index++)
        {
            if (bytes[(int)index] == (byte)'\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    private static int ToOneBased(long? zeroBased) => zeroBased is null
        ? 1
        : checked((int)zeroBased.Value + 1);

    private static string EscapeJsonPointerSegment(string segment) => segment
        .Replace("~", "~0", StringComparison.Ordinal)
        .Replace("/", "~1", StringComparison.Ordinal);

    private sealed record JsonConfigurationDefinitionSite(
        string KeyPath,
        string JsonPointer,
        string RepositoryRelativePath,
        int Line,
        int Column);

    private sealed record ParsedJsonConfigurationFile(
        GraphNodeFact FileNode,
        IReadOnlyList<JsonConfigurationDefinitionSite> Definitions,
        IReadOnlyList<JsonConfigurationDefinitionDiagnostic> Diagnostics);
}
