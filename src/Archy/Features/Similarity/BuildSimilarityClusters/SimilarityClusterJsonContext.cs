using System.Text.Json.Serialization;
using Archy.Features.Similarity.RetrieveHybridCandidates;

namespace Archy.Features.Similarity.BuildSimilarityClusters;

[JsonSerializable(typeof(SimilarityEvidenceKind[]))]
internal sealed partial class SimilarityClusterJsonContext : JsonSerializerContext;
