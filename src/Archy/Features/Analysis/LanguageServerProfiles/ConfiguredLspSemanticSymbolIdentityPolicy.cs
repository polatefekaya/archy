using Archy.Features.Analysis.LanguageSemanticAdapters;
using Archy.Features.Configuration.LoadEffectiveConfiguration;

namespace Archy.Features.Analysis.LanguageServerProfiles;

/// <summary>Maps standard LSP SymbolKind integers using only the selected declarative profile.</summary>
public sealed class ConfiguredLspSemanticSymbolIdentityPolicy : ILspSemanticSymbolIdentityPolicy
{
    private readonly Dictionary<int, SemanticSymbolKind> semanticKinds;
    private readonly string identityPrefix;

    public ConfiguredLspSemanticSymbolIdentityPolicy(LanguageServerProfileConfiguration profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        AdapterId = profile.Id;
        identityPrefix = profile.SymbolIdentityPrefix;
        var mappings = new Dictionary<int, SemanticSymbolKind>();
        foreach (var mapping in profile.SymbolKinds)
        {
            if (!TrySemanticKind(mapping.SemanticKind, out var semanticKind))
            {
                throw new ArgumentException($"Language-server profile '{profile.Id}' has unsupported semantic kind '{mapping.SemanticKind}'.", nameof(profile));
            }

            foreach (var lspKind in mapping.LspKinds)
            {
                if (!mappings.TryAdd(lspKind, semanticKind))
                {
                    throw new ArgumentException($"Language-server profile '{profile.Id}' maps LSP SymbolKind '{lspKind}' more than once.", nameof(profile));
                }
            }
        }

        semanticKinds = mappings;
    }

    public string AdapterId { get; }

    public SemanticSymbol? CreateSymbol(string repositoryRelativePath, string qualifiedName, string displayName, int lspSymbolKind, string? containerCanonicalId, SemanticSourceRange range) =>
        semanticKinds.TryGetValue(lspSymbolKind, out var kind)
            ? new SemanticSymbol($"{identityPrefix}:{qualifiedName}@{repositoryRelativePath}:{range.StartLine}:{range.StartColumn}", displayName, kind, containerCanonicalId, "unknown", range)
            : null;

    private static bool TrySemanticKind(string value, out SemanticSymbolKind kind) => value switch
    {
        "namespace" => Assign(SemanticSymbolKind.Namespace, out kind),
        "type" => Assign(SemanticSymbolKind.Type, out kind),
        "method" => Assign(SemanticSymbolKind.Method, out kind),
        "property" => Assign(SemanticSymbolKind.Property, out kind),
        "field" => Assign(SemanticSymbolKind.Field, out kind),
        "event" => Assign(SemanticSymbolKind.Event, out kind),
        "parameter" => Assign(SemanticSymbolKind.Parameter, out kind),
        _ => Assign(default, out kind, false),
    };

    private static bool Assign(SemanticSymbolKind value, out SemanticSymbolKind target, bool success = true)
    {
        target = value;
        return success;
    }
}
