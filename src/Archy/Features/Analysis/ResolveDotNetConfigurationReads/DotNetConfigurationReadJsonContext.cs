using System.Text.Json.Serialization;

namespace Archy.Features.Analysis.ResolveDotNetConfigurationReads;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DotNetConfigurationReadEvidence))]
[JsonSerializable(typeof(DotNetConfigurationKeyEvidence))]
internal sealed partial class DotNetConfigurationReadJsonContext : JsonSerializerContext;

internal sealed record DotNetConfigurationReadEvidence(
    string AccessForm,
    string KeyPath,
    string RawKey,
    string SourceStableId,
    string RepositoryRelativePath,
    int Line,
    int Column);

internal sealed record DotNetConfigurationKeyEvidence(
    string KeyPath,
    IReadOnlyList<DotNetConfigurationReadEvidence> Reads);
