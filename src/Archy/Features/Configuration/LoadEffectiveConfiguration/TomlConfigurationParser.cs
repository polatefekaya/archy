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
        reader.EnsureAllowed(root, "root", "schema_version", "workspace", "storage", "language_servers", "providers", "layers", "model", "sidecars", "scope", "health_weights");

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

        var languageServers = reader.OptionalTable(root, "language_servers", "root");
        var csharpLanguageServer = languageServers is null
            ? null
            : reader.OptionalTable(languageServers, "csharp", "language_servers");
        if (languageServers is not null)
        {
            reader.EnsureAllowed(languageServers, "language_servers", "csharp");
        }

        if (csharpLanguageServer is not null)
        {
            reader.EnsureAllowed(csharpLanguageServer, "language_servers.csharp", "command", "args");
        }

        var providers = reader.OptionalTable(root, "providers", "root");
        if (providers is not null)
        {
            reader.EnsureAllowed(providers, "providers", "dependency_injection", "messaging", "entity_framework_core", "cache", "configuration");
        }

        var model = reader.OptionalTable(root, "model", "root");
        if (model is not null)
        {
            reader.EnsureAllowed(model, "model", "provider", "summary_model", "embedding_model", "max_requests_per_run", "max_tokens_per_run");
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

        var layers = reader.OptionalTableArray(root, "layers", "root");
        var parsedLayers = ParseLayers(reader, layers);

        var modelProvider = reader.OptionalString(model, "provider", "model");
        if (modelProvider is not null && modelProvider is not ("openai" or "disabled"))
        {
            reader.AddError("model.provider must be either 'openai' or 'disabled'.");
        }

        var layer = new ArchyConfigurationLayer(
            WorkspaceDisplayName: reader.OptionalString(workspace, "display_name", "workspace"),
            LocalStateRootPath: reader.OptionalString(storage, "state_root", "storage"),
            CSharpLanguageServerCommand: reader.OptionalString(csharpLanguageServer, "command", "language_servers.csharp"),
            CSharpLanguageServerArguments: reader.OptionalStringArray(csharpLanguageServer, "args", "language_servers.csharp"),
            DependencyInjectionProviderEnabled: reader.OptionalBoolean(providers, "dependency_injection", "providers"),
            MessagingProviderEnabled: reader.OptionalBoolean(providers, "messaging", "providers"),
            EntityFrameworkCoreProviderEnabled: reader.OptionalBoolean(providers, "entity_framework_core", "providers"),
            CacheProviderEnabled: reader.OptionalBoolean(providers, "cache", "providers"),
            ConfigurationProviderEnabled: reader.OptionalBoolean(providers, "configuration", "providers"),
            Layers: parsedLayers,
            ModelProvider: modelProvider,
            SummaryModel: reader.OptionalString(model, "summary_model", "model"),
            EmbeddingModel: reader.OptionalString(model, "embedding_model", "model"),
            MaxRequestsPerRun: reader.OptionalInt(model, "max_requests_per_run", "model"),
            MaxTokensPerRun: reader.OptionalInt(model, "max_tokens_per_run", "model"),
            JscpdCommand: reader.OptionalString(sidecars, "jscpd_command", "sidecars"),
            LouvainCommand: reader.OptionalString(sidecars, "louvain_command", "sidecars"),
            ScopeInclude: reader.OptionalStringArray(scope, "include", "scope"),
            ScopeExclude: reader.OptionalStringArray(scope, "exclude", "scope"),
            ArchitectureWeight: reader.OptionalDouble(healthWeights, "architecture", "health_weights"),
            DuplicatesWeight: reader.OptionalDouble(healthWeights, "duplicates", "health_weights"),
            DocumentationWeight: reader.OptionalDouble(healthWeights, "documentation", "health_weights"),
            DecisionsWeight: reader.OptionalDouble(healthWeights, "decisions", "health_weights"));

        ValidateLayer(reader, layer);

        return reader.Complete(layer);
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

                if (include.Length == 0)
                {
                    reader.AddError($"{location}.include must contain at least one repository-relative pattern.");
                }

                parsedLayers.Add(new LayerRuleConfiguration(name, include, mayDependOn));
            }

            index++;
        }

        return [.. parsedLayers];
    }

    private static void ValidateLayer(TomlConfigurationReader reader, ArchyConfigurationLayer layer)
    {
        reader.ValidateOptionalNonEmpty(layer.WorkspaceDisplayName, "workspace.display_name");
        reader.ValidateOptionalNonEmpty(layer.LocalStateRootPath, "storage.state_root");
        reader.ValidateOptionalNonEmpty(layer.CSharpLanguageServerCommand, "language_servers.csharp.command");
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
