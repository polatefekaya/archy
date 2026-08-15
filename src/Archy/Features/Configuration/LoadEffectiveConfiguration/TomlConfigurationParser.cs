using Archy.SharedKernel.Primitives;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Archy.Features.Configuration.LoadEffectiveConfiguration;

public sealed class TomlConfigurationParser : ITomlConfigurationParser
{
    public Result<ArchyConfigurationLayer> Parse(string document, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        if (!TomlSerializer.TryDeserialize(document, ArchyTomlContext.Default, out TomlTable? root) || root is null)
        {
            return ResultFactory.Failure<ArchyConfigurationLayer>(
                Problem.Validation($"Configuration '{sourcePath}' is not valid TOML 1.1."));
        }

        var reader = new TomlConfigurationReader(sourcePath);
        reader.EnsureAllowed(root, "root", "schema_version", "workspace", "storage", "language_server_profiles", "providers", "provider_patterns", "layers", "enforcement", "model", "memory", "sidecars", "scope", "health_weights", "similarity");

        var schemaVersion = reader.OptionalInt(root, "schema_version", "root");
        if (schemaVersion is null)
        {
            reader.AddError("A configuration file must declare schema_version = 1.");
        }
        else if (schemaVersion != 1)
        {
            reader.AddError($"schema_version must be 1; found {schemaVersion.Value}.");
        }

        var workspace = reader.OptionalTable(root, "workspace", "root");
        if (workspace is not null)
        {
            reader.EnsureAllowed(workspace, "workspace", "display_name");
        }

        var storage = reader.OptionalTable(root, "storage", "root");
        if (storage is not null)
        {
            reader.EnsureAllowed(storage, "storage", "state_root");
        }

        var providers = reader.OptionalTable(root, "providers", "root");
        if (providers is not null)
        {
            reader.EnsureAllowed(providers, "providers", "dependency_injection", "messaging", "entity_framework_core", "cache", "configuration");
        }

        var model = reader.OptionalTable(root, "model", "root");
        if (model is not null)
        {
            reader.EnsureAllowed(model, "model", "provider", "summary_model", "embedding_model", "max_requests_per_run", "max_tokens_per_run", "max_cost_usd_per_run", "max_concurrent_requests", "rate_limit_cooldown_seconds");
        }

        var memory = reader.OptionalTable(root, "memory", "root");
        if (memory is not null)
        {
            reader.EnsureAllowed(memory, "memory", "include_generated_nodes", "important_module_paths", "source_sharing");
        }

        var sidecars = reader.OptionalTable(root, "sidecars", "root");
        if (sidecars is not null)
        {
            reader.EnsureAllowed(sidecars, "sidecars", "jscpd_command", "louvain_command");
        }

        var scope = reader.OptionalTable(root, "scope", "root");
        if (scope is not null)
        {
            reader.EnsureAllowed(scope, "scope", "include", "exclude");
        }

        var healthWeights = reader.OptionalTable(root, "health_weights", "root");
        if (healthWeights is not null)
        {
            reader.EnsureAllowed(healthWeights, "health_weights", "architecture", "duplicates", "documentation", "decisions");
        }

        var similarity = reader.OptionalTable(root, "similarity", "root");
        if (similarity is not null)
        {
            reader.EnsureAllowed(similarity, "similarity", "policy_version", "embedding_weight", "symbol_weight", "signature_weight", "dependency_neighborhood_weight", "file_context_weight", "module_context_weight");
        }

        var enforcement = reader.OptionalTable(root, "enforcement", "root");
        if (enforcement is not null)
        {
            reader.EnsureAllowed(enforcement, "enforcement", "hard_edge_kinds");
        }

        var layers = reader.OptionalTableArray(root, "layers", "root");
        var parsedLayers = ParseLayers(reader, layers);
        var patterns = reader.OptionalTableArray(root, "provider_patterns", "root");
        var parsedPatterns = ParseProviderPatterns(reader, patterns);
        var languageServerProfiles = reader.OptionalTableArray(root, "language_server_profiles", "root");
        var parsedLanguageServerProfiles = ParseLanguageServerProfiles(reader, languageServerProfiles);

        var modelProvider = reader.OptionalString(model, "provider", "model");
        if (modelProvider is not null && modelProvider is not ("openai" or "disabled"))
        {
            reader.AddError("model.provider must be either 'openai' or 'disabled'.");
        }

        var sourceSharing = ParseAiSourceSharingMode(reader, reader.OptionalString(memory, "source_sharing", "memory"));

        var layer = new ArchyConfigurationLayer(
            WorkspaceDisplayName: reader.OptionalString(workspace, "display_name", "workspace"),
            LocalStateRootPath: reader.OptionalString(storage, "state_root", "storage"),
            LanguageServerProfiles: parsedLanguageServerProfiles,
            DependencyInjectionProviderEnabled: reader.OptionalBoolean(providers, "dependency_injection", "providers"),
            MessagingProviderEnabled: reader.OptionalBoolean(providers, "messaging", "providers"),
            EntityFrameworkCoreProviderEnabled: reader.OptionalBoolean(providers, "entity_framework_core", "providers"),
            CacheProviderEnabled: reader.OptionalBoolean(providers, "cache", "providers"),
            ConfigurationProviderEnabled: reader.OptionalBoolean(providers, "configuration", "providers"),
            ProviderPatterns: parsedPatterns,
            Layers: parsedLayers,
            EnforcementHardEdgeKinds: reader.OptionalStringArray(enforcement, "hard_edge_kinds", "enforcement"),
            ModelProvider: modelProvider,
            SummaryModel: reader.OptionalString(model, "summary_model", "model"),
            EmbeddingModel: reader.OptionalString(model, "embedding_model", "model"),
            MaxRequestsPerRun: reader.OptionalInt(model, "max_requests_per_run", "model"),
            MaxTokensPerRun: reader.OptionalInt(model, "max_tokens_per_run", "model"),
            MaxCostUsdPerRun: reader.OptionalDouble(model, "max_cost_usd_per_run", "model"),
            MaxConcurrentModelRequests: reader.OptionalInt(model, "max_concurrent_requests", "model"),
            ModelRateLimitCooldownSeconds: reader.OptionalInt(model, "rate_limit_cooldown_seconds", "model"),
            IncludeGeneratedMemoryNodes: reader.OptionalBoolean(memory, "include_generated_nodes", "memory"),
            ImportantMemoryModulePaths: reader.OptionalStringArray(memory, "important_module_paths", "memory"),
            AiSourceSharingMode: sourceSharing,
            JscpdCommand: reader.OptionalString(sidecars, "jscpd_command", "sidecars"),
            LouvainCommand: reader.OptionalString(sidecars, "louvain_command", "sidecars"),
            ScopeInclude: reader.OptionalStringArray(scope, "include", "scope"),
            ScopeExclude: reader.OptionalStringArray(scope, "exclude", "scope"),
            ArchitectureWeight: reader.OptionalDouble(healthWeights, "architecture", "health_weights"),
            DuplicatesWeight: reader.OptionalDouble(healthWeights, "duplicates", "health_weights"),
            DocumentationWeight: reader.OptionalDouble(healthWeights, "documentation", "health_weights"),
            DecisionsWeight: reader.OptionalDouble(healthWeights, "decisions", "health_weights"),
            SimilarityPolicyVersion: reader.OptionalString(similarity, "policy_version", "similarity"),
            SimilarityEmbeddingWeight: reader.OptionalDouble(similarity, "embedding_weight", "similarity"),
            SimilaritySymbolWeight: reader.OptionalDouble(similarity, "symbol_weight", "similarity"),
            SimilaritySignatureWeight: reader.OptionalDouble(similarity, "signature_weight", "similarity"),
            SimilarityDependencyNeighborhoodWeight: reader.OptionalDouble(similarity, "dependency_neighborhood_weight", "similarity"),
            SimilarityFileContextWeight: reader.OptionalDouble(similarity, "file_context_weight", "similarity"),
            SimilarityModuleContextWeight: reader.OptionalDouble(similarity, "module_context_weight", "similarity"));

        ValidateLayer(reader, layer);

        return reader.Complete(layer);
    }

    private static AiSourceSharingMode? ParseAiSourceSharingMode(TomlConfigurationReader reader, string? value)
    {
        if (value is null)
        {
            return null;
        }

        return value switch
        {
            "disabled" => AiSourceSharingMode.Disabled,
            "summaries_only" => AiSourceSharingMode.SummariesOnly,
            "summaries_and_embeddings" => AiSourceSharingMode.SummariesAndEmbeddings,
            _ => AddInvalidAiSourceSharingMode(reader),
        };
    }

    private static AiSourceSharingMode? AddInvalidAiSourceSharingMode(TomlConfigurationReader reader)
    {
        reader.AddError("memory.source_sharing must be disabled, summaries_only, or summaries_and_embeddings.");
        return null;
    }

    private static LayerRuleConfiguration[]? ParseLayers(
        TomlConfigurationReader reader,
        TomlTableArray? layers)
    {
        if (layers is null)
        {
            return null;
        }

        var parsedLayers = new List<LayerRuleConfiguration>(layers.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;

        foreach (var layer in layers)
        {
            var location = $"layers[{index}]";
            reader.EnsureAllowed(layer, location, "name", "include", "may_depend_on");
            var name = reader.RequiredString(layer, "name", location);
            var include = reader.OptionalStringArray(layer, "include", location) ?? [];
            var mayDependOn = reader.OptionalStringArray(layer, "may_depend_on", location) ?? [];

            if (name is not null)
            {
                if (!names.Add(name))
                {
                    reader.AddError($"{location}.name duplicates the layer '{name}'.");
                }

                if (!LayerRuleConfigurationValidator.IsLayerName(name))
                {
                    reader.AddError($"{location}.name must be a trimmed, non-control layer name no longer than 128 characters.");
                }

                if (include.Length == 0)
                {
                    reader.AddError($"{location}.include must contain at least one repository-relative pattern.");
                }
                else if (include.Any(static pattern => !LayerRuleConfigurationValidator.IsRepositoryRelativePattern(pattern)) || include.Distinct(StringComparer.Ordinal).Count() != include.Length)
                {
                    reader.AddError($"{location}.include must contain unique repository-relative glob patterns.");
                }

                if (mayDependOn.Any(static dependency => !LayerRuleConfigurationValidator.IsLayerName(dependency)) ||
                    mayDependOn.Distinct(StringComparer.Ordinal).Count() != mayDependOn.Length ||
                    mayDependOn.Contains(name, StringComparer.Ordinal))
                {
                    reader.AddError($"{location}.may_depend_on must contain unique declared layer names other than itself.");
                }

                parsedLayers.Add(new LayerRuleConfiguration(name, include, mayDependOn));
            }

            index++;
        }

        var declaredNames = parsedLayers.Select(static layer => layer.Name).ToHashSet(StringComparer.Ordinal);
        for (var layerIndex = 0; layerIndex < parsedLayers.Count; layerIndex++)
        {
            var layer = parsedLayers[layerIndex];
            foreach (var dependency in layer.MayDependOn.Where(dependency => !declaredNames.Contains(dependency)))
            {
                reader.AddError($"layers[{layerIndex}].may_depend_on references undeclared layer '{dependency}'.");
            }
        }

        return [.. parsedLayers];
    }

    private static ProviderPatternConfiguration[]? ParseProviderPatterns(
        TomlConfigurationReader reader,
        TomlTableArray? patterns)
    {
        if (patterns is null)
        {
            return null;
        }

        var parsed = new List<ProviderPatternConfiguration>(patterns.Count);
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var pattern in patterns)
        {
            var location = $"provider_patterns[{index}]";
            reader.EnsureAllowed(pattern, location, "id", "framework", "match_kind", "member", "type", "capture_name", "capture_argument_index");
            var id = reader.RequiredString(pattern, "id", location);
            var framework = reader.RequiredString(pattern, "framework", location);
            var matchKind = reader.RequiredString(pattern, "match_kind", location);
            var member = reader.RequiredString(pattern, "member", location);
            var captureName = reader.OptionalString(pattern, "capture_name", location);
            var captureArgumentIndex = reader.OptionalInt(pattern, "capture_argument_index", location);
            if (id is not null && framework is not null && matchKind is not null && member is not null)
            {
                if (!identifiers.Add(id))
                {
                    reader.AddError($"{location}.id duplicates provider pattern '{id}'.");
                }

                if (matchKind is not ("invocation" or "type" or "attribute"))
                {
                    reader.AddError($"{location}.match_kind must be invocation, type, or attribute.");
                }

                if (captureArgumentIndex is < 0 || (captureArgumentIndex is null && captureName is not null) || (captureArgumentIndex is not null && captureName is null))
                {
                    reader.AddError($"{location}.capture_name and capture_argument_index must be specified together, with a non-negative index.");
                }

                parsed.Add(new ProviderPatternConfiguration(
                    id,
                    framework,
                    matchKind,
                    member,
                    reader.OptionalString(pattern, "type", location),
                    captureName,
                    captureArgumentIndex));
            }

            index++;
        }

        return [.. parsed];
    }

    private static LanguageServerProfileConfiguration[]? ParseLanguageServerProfiles(
        TomlConfigurationReader reader,
        TomlTableArray? profiles)
    {
        if (profiles is null)
        {
            return null;
        }

        var parsed = new List<LanguageServerProfileConfiguration>(profiles.Count);
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var profile in profiles)
        {
            var location = $"language_server_profiles[{index}]";
            reader.EnsureAllowed(profile, location, "id", "language_id", "extensions", "markers", "command", "args", "symbol_identity_prefix", "symbol_kinds", "max_symbol_queries");
            var id = reader.RequiredString(profile, "id", location);
            var languageId = reader.RequiredString(profile, "language_id", location);
            var extensions = reader.OptionalStringArray(profile, "extensions", location) ?? [];
            var markers = reader.OptionalStringArray(profile, "markers", location) ?? [];
            var command = reader.RequiredString(profile, "command", location);
            var arguments = reader.OptionalStringArray(profile, "args", location) ?? [];
            var symbolIdentityPrefix = reader.RequiredString(profile, "symbol_identity_prefix", location);
            var maxSymbolQueries = reader.OptionalInt(profile, "max_symbol_queries", location) ?? 10_000;
            var symbolKinds = ParseLanguageServerSymbolKinds(reader, reader.OptionalTable(profile, "symbol_kinds", location), location);

            if (id is not null && languageId is not null && command is not null && symbolIdentityPrefix is not null)
            {
                if (!identifiers.Add(id))
                {
                    reader.AddError($"{location}.id duplicates language-server profile '{id}'.");
                }

                if (extensions.Length == 0 || extensions.Any(static extension => string.IsNullOrWhiteSpace(extension) || extension[0] != '.' || extension.IndexOfAny(['/', '\\', '*', '?']) >= 0))
                {
                    reader.AddError($"{location}.extensions must contain one or more file extensions such as '.cs'.");
                }

                if (markers.Any(static marker => string.IsNullOrWhiteSpace(marker) || Path.IsPathFullyQualified(marker) || marker.Split(['/', '\\'], StringSplitOptions.None).Any(static segment => segment == "..")))
                {
                    reader.AddError($"{location}.markers must contain only repository-relative marker patterns.");
                }

                if (symbolKinds.Length == 0)
                {
                    reader.AddError($"{location}.symbol_kinds must map at least one semantic kind to LSP kind numbers.");
                }

                if (maxSymbolQueries is < 1 or > 100_000)
                {
                    reader.AddError($"{location}.max_symbol_queries must be between 1 and 100000.");
                }

                parsed.Add(new LanguageServerProfileConfiguration(id, languageId, extensions, markers, command, arguments, symbolIdentityPrefix, symbolKinds, maxSymbolQueries));
            }

            index++;
        }

        return [.. parsed];
    }

    private static LanguageServerSymbolKindMapping[] ParseLanguageServerSymbolKinds(
        TomlConfigurationReader reader,
        TomlTable? symbolKinds,
        string profileLocation)
    {
        if (symbolKinds is null)
        {
            reader.AddError($"{profileLocation}.symbol_kinds is required.");
            return [];
        }

        const string location = "symbol_kinds";
        var allowed = new[] { "namespace", "type", "method", "property", "field", "event", "parameter" };
        reader.EnsureAllowed(symbolKinds, $"{profileLocation}.{location}", allowed);
        var mappings = new List<LanguageServerSymbolKindMapping>();
        foreach (var semanticKind in allowed)
        {
            var kinds = reader.OptionalIntArray(symbolKinds, semanticKind, $"{profileLocation}.{location}");
            if (kinds is null)
            {
                continue;
            }

            if (kinds.Length == 0 || kinds.Any(static kind => kind is < 1 or > 26) || kinds.Distinct().Count() != kinds.Length)
            {
                reader.AddError($"{profileLocation}.{location}.{semanticKind} must contain distinct LSP SymbolKind values from 1 through 26.");
            }

            mappings.Add(new LanguageServerSymbolKindMapping(semanticKind, kinds));
        }

        return [.. mappings];
    }

    private static void ValidateLayer(TomlConfigurationReader reader, ArchyConfigurationLayer layer)
    {
        reader.ValidateOptionalNonEmpty(layer.WorkspaceDisplayName, "workspace.display_name");
        reader.ValidateOptionalNonEmpty(layer.LocalStateRootPath, "storage.state_root");
        reader.ValidateOptionalNonEmpty(layer.SummaryModel, "model.summary_model");
        reader.ValidateOptionalNonEmpty(layer.EmbeddingModel, "model.embedding_model");
        reader.ValidateOptionalNonEmpty(layer.JscpdCommand, "sidecars.jscpd_command");
        reader.ValidateOptionalNonEmpty(layer.LouvainCommand, "sidecars.louvain_command");
        reader.ValidateRelativePatterns(layer.ScopeInclude, "scope.include");
        reader.ValidateRelativePatterns(layer.ScopeExclude, "scope.exclude");

        if (layer.MaxRequestsPerRun is < 0)
        {
            reader.AddError("model.max_requests_per_run must be zero or greater.");
        }

        if (layer.MaxTokensPerRun is < 0)
        {
            reader.AddError("model.max_tokens_per_run must be zero or greater.");
        }

        reader.ValidateNonNegativeWeight(layer.ArchitectureWeight, "health_weights.architecture");
        reader.ValidateNonNegativeWeight(layer.DuplicatesWeight, "health_weights.duplicates");
        reader.ValidateNonNegativeWeight(layer.DocumentationWeight, "health_weights.documentation");
        reader.ValidateNonNegativeWeight(layer.DecisionsWeight, "health_weights.decisions");
        reader.ValidateOptionalNonEmpty(layer.SimilarityPolicyVersion, "similarity.policy_version");
        reader.ValidateNonNegativeWeight(layer.SimilarityEmbeddingWeight, "similarity.embedding_weight");
        reader.ValidateNonNegativeWeight(layer.SimilaritySymbolWeight, "similarity.symbol_weight");
        reader.ValidateNonNegativeWeight(layer.SimilaritySignatureWeight, "similarity.signature_weight");
        reader.ValidateNonNegativeWeight(layer.SimilarityDependencyNeighborhoodWeight, "similarity.dependency_neighborhood_weight");
        reader.ValidateNonNegativeWeight(layer.SimilarityFileContextWeight, "similarity.file_context_weight");
        reader.ValidateNonNegativeWeight(layer.SimilarityModuleContextWeight, "similarity.module_context_weight");
    }

    // Tomlyn's built-in untyped TOML model only needs one explicit type-info root.
    // Keeping this metadata hand-written avoids the reflection fallback and makes the
    // parser safe for the host's trimming and Native AOT gates.
    private sealed class ArchyTomlContext : TomlSerializerContext
    {
        private TomlTypeInfo<TomlTable>? _tomlTable;

        public static ArchyTomlContext Default { get; } = new();

        public TomlTypeInfo<TomlTable> TomlTable =>
            _tomlTable ??= GetBuiltInTypeInfo<TomlTable>(Options);

        public override TomlTypeInfo? GetTypeInfo(Type type, TomlSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(options);

            if (type != typeof(TomlTable))
            {
                return null;
            }

            return ReferenceEquals(options, Options)
                ? TomlTable
                : GetBuiltInTypeInfo<TomlTable>(options);
        }
    }

    private sealed class TomlConfigurationReader(string sourcePath)
    {
        private readonly List<string> _errors = [];

        public void AddError(string message)
        {
            _errors.Add($"{sourcePath}: {message}");
        }

        public Result<ArchyConfigurationLayer> Complete(ArchyConfigurationLayer layer)
        {
            return _errors.Count == 0
                ? ResultFactory.Success(layer)
                : ResultFactory.Failure<ArchyConfigurationLayer>(
                    Problem.Validation(string.Join(Environment.NewLine, _errors)));
        }

        public void EnsureAllowed(TomlTable table, string location, params string[] allowedKeys)
        {
            foreach (var key in table.Keys)
            {
                if (!allowedKeys.Contains(key, StringComparer.Ordinal))
                {
                    var message = key is "api_key" or "openai_api_key" or "secret"
                        ? $"{location}.{key} is forbidden. Configure API credentials through OPENAI_API_KEY or macOS Keychain, never TOML."
                        : $"{location}.{key} is not recognized by schema version 1.";
                    AddError(message);
                }
            }
        }

        public TomlTable? OptionalTable(TomlTable? table, string key, string location)
        {
            if (table is null || !table.TryGetValue(key, out var value))
            {
                return null;
            }

            if (value is TomlTable nestedTable)
            {
                return nestedTable;
            }

            AddError($"{location}.{key} must be a TOML table.");
            return null;
        }

        public TomlTableArray? OptionalTableArray(TomlTable table, string key, string location)
        {
            if (!table.TryGetValue(key, out var value))
            {
                return null;
            }

            if (value is TomlTableArray tableArray)
            {
                return tableArray;
            }

            AddError($"{location}.{key} must be an array of tables.");
            return null;
        }

        public string? RequiredString(TomlTable table, string key, string location)
        {
            var value = OptionalString(table, key, location);
            if (value is null)
            {
                AddError($"{location}.{key} is required.");
            }

            return value;
        }

        public string? OptionalString(TomlTable? table, string key, string location)
        {
            if (table is null || !table.TryGetValue(key, out var value))
            {
                return null;
            }

            if (value is string stringValue)
            {
                return stringValue;
            }

            AddError($"{location}.{key} must be a string.");
            return null;
        }

        public string[]? OptionalStringArray(TomlTable? table, string key, string location)
        {
            if (table is null || !table.TryGetValue(key, out var value))
            {
                return null;
            }

            if (value is not TomlArray array)
            {
                AddError($"{location}.{key} must be an array of strings.");
                return null;
            }

            var strings = new List<string>(array.Count);
            foreach (var item in array)
            {
                if (item is string stringValue)
                {
                    strings.Add(stringValue);
                    continue;
                }

                AddError($"{location}.{key} must contain only strings.");
                return null;
            }

            return [.. strings];
        }

        public int[]? OptionalIntArray(TomlTable? table, string key, string location)
        {
            if (table is null || !table.TryGetValue(key, out var value))
            {
                return null;
            }

            if (value is not TomlArray array)
            {
                AddError($"{location}.{key} must be an array of 32-bit integers.");
                return null;
            }

            var integers = new List<int>(array.Count);
            foreach (var item in array)
            {
                if (item is long integer && integer is >= int.MinValue and <= int.MaxValue)
                {
                    integers.Add((int)integer);
                    continue;
                }

                AddError($"{location}.{key} must contain only 32-bit integers.");
                return null;
            }

            return [.. integers];
        }

        public bool? OptionalBoolean(TomlTable? table, string key, string location)
        {
            if (table is null || !table.TryGetValue(key, out var value))
            {
                return null;
            }

            if (value is bool booleanValue)
            {
                return booleanValue;
            }

            AddError($"{location}.{key} must be true or false.");
            return null;
        }

        public int? OptionalInt(TomlTable? table, string key, string location)
        {
            if (table is null || !table.TryGetValue(key, out var value))
            {
                return null;
            }

            if (value is long longValue && longValue is >= int.MinValue and <= int.MaxValue)
            {
                return (int)longValue;
            }

            AddError($"{location}.{key} must be a 32-bit integer.");
            return null;
        }

        public double? OptionalDouble(TomlTable? table, string key, string location)
        {
            if (table is null || !table.TryGetValue(key, out var value))
            {
                return null;
            }

            switch (value)
            {
                case long longValue:
                    return longValue;
                case double doubleValue when !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue):
                    return doubleValue;
                default:
                    AddError($"{location}.{key} must be a finite number.");
                    return null;
            }
        }

        public void ValidateOptionalNonEmpty(string? value, string location)
        {
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                AddError($"{location} cannot be empty.");
            }
        }

        public void ValidateRelativePatterns(string[]? patterns, string location)
        {
            if (patterns is null)
            {
                return;
            }

            foreach (var pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    AddError($"{location} cannot contain an empty pattern.");
                }
                else if (Path.IsPathFullyQualified(pattern) || HasParentTraversal(pattern))
                {
                    AddError($"{location} must contain repository-relative patterns, not '{pattern}'.");
                }
            }
        }

        private static bool HasParentTraversal(string pattern)
        {
            return pattern.Split(['/', '\\'], StringSplitOptions.None)
                .Any(static segment => string.Equals(segment, "..", StringComparison.Ordinal));
        }

        public void ValidateNonNegativeWeight(double? value, string location)
        {
            if (value is < 0)
            {
                AddError($"{location} must be zero or greater.");
            }
        }
    }
}
