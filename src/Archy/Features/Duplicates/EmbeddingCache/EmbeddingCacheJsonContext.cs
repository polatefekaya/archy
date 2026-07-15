using System.Text.Json.Serialization;

namespace Archy.Features.Duplicates.EmbeddingCache;

[JsonSerializable(typeof(float[]))]
internal sealed partial class EmbeddingCacheJsonContext : JsonSerializerContext;
