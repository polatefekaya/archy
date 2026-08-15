using System.Text.Json;
using System.Text.Json.Serialization;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Similarity.BuildSimilarityClusters;
using Archy.Features.Similarity.DetectReintroducedCapability;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.ResolveWorkspaceState;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Similarity.RetrieveHybridCandidates;
using Mediator;

namespace Archy.Features.CommandLine.Similarity;

public static partial class SimilarityCli
{
    public static async Task<int> RunAsync(string[] args, IMediator mediator, CancellationToken ct)
    {
        if (args.Length == 0 || args[0] is not ("clusters" or "reintroduced")) return Usage(); var options = Parse(args[1..]); if (options is null) return Usage();
        var state = await mediator.Send(new ResolveWorkspaceStateQuery(options.Path, options.Config, options.StateRoot), ct); if (!state.IsSuccess) return Error(state.Problem!.Message);
        var locks = new WorkspaceLockManager(TimeProvider.System);
        if (args[0] == "clusters")
        {
            if (options.Build)
            {
                var graphSnapshot = await new GraphRevisionSnapshotReader(locks).ReadActiveAsync(state.Value, ct); if (!graphSnapshot.IsSuccess) return Error(graphSnapshot.Problem!.Message);
                if (graphSnapshot.Value is null) { Console.WriteLine("No active graph revision exists; similarity clustering abstains."); return 0; }
                var configuration = await mediator.Send(new LoadEffectiveConfigurationQuery(options.Path, options.Config, options.StateRoot), ct);
                if (!configuration.IsSuccess) return Error(configuration.Problem!.Message);
                var similarity = configuration.Value!.Configuration.Similarity;
                var policy = new HybridSimilarityPolicy(similarity.PolicyVersion, new HybridSimilarityWeights(similarity.EmbeddingWeight, similarity.SymbolWeight, similarity.SignatureWeight, similarity.DependencyNeighborhoodWeight, similarity.FileContextWeight, similarity.ModuleContextWeight));
                var build = SimilarityClusterBuilder.Build(graphSnapshot.Value, policy);
                if (build.Clusters.Count == 0) { Console.WriteLine("No multi-member similarity clusters met the conservative evidence threshold."); return 0; }
                var recorded = await new SimilarityClusterRepository(TimeProvider.System, locks).RecordAsync(state.Value, build, ct); if (!recorded.IsSuccess) return Error(recorded.Problem!.Message);
                if (options.Json) Console.WriteLine(JsonSerializer.Serialize(recorded.Value, SimilarityJsonContext.Default.PersistedSimilarityClusterRevision)); else Console.WriteLine($"Recorded {recorded.Value!.Clusters.Count} similarity cluster(s) at graph revision {recorded.Value.GraphRevision}.");
                return 0;
            }
            var result = await new SimilarityClusterRepository(TimeProvider.System, locks).ReadLatestAsync(state.Value, options.Revision, ct); if (!result.IsSuccess) return Error(result.Problem!.Message);
            if (options.Json) Console.WriteLine(JsonSerializer.Serialize(result.Value, SimilarityJsonContext.Default.PersistedSimilarityClusterRevision)); else Console.WriteLine(result.Value is null ? "No persisted similarity cluster revision is available." : $"Similarity clusters at graph revision {result.Value.GraphRevision}: {result.Value.Clusters.Count} cluster(s).");
            return 0;
        }
        if (string.IsNullOrWhiteSpace(options.StableId)) return Usage();
        var snapshot = await new GraphRevisionSnapshotReader(locks).ReadActiveAsync(state.Value, ct); if (!snapshot.IsSuccess) return Error(snapshot.Problem!.Message); if (snapshot.Value is null) { Console.WriteLine("No active graph revision exists; historical detection abstains."); return 0; }
        var reintroduced = await new ReintroducedCapabilityDetector(locks).FindAsync(state.Value, snapshot.Value, options.StableId, ct); if (!reintroduced.IsSuccess) return Error(reintroduced.Problem!.Message);
        if (options.Json) Console.WriteLine(JsonSerializer.Serialize(reintroduced.Value, SimilarityJsonContext.Default.ReintroducedCapabilityResult)); else Console.WriteLine(reintroduced.Value!.Abstained ? reintroduced.Value.AbstentionReason : $"Found {reintroduced.Value.Matches.Count} historical reintroduction match(es).");
        return 0;
    }
    private static int Error(string message) { Console.Error.WriteLine(message); return 2; }
    private static int Usage() { Console.Error.WriteLine("Usage: archy similarity clusters [--build] [--revision <n>] [--path <path>] [--state-root <path>] [--config <path>] [--json]\n       archy similarity reintroduced --stable-id <id> [--path <path>] [--state-root <path>] [--config <path>] [--json]"); return 64; }
    private sealed record Options(string Path, string? StateRoot, string? Config, long? Revision, string? StableId, bool Json, bool Build);
    private static Options? Parse(string[] args) { var path = Directory.GetCurrentDirectory(); string? state = null, config = null, id = null; long? revision = null; var json = false; var build = false; for (var i = 0; i < args.Length; i++) { if (args[i] == "--json") { json = true; continue; } if (args[i] == "--build") { build = true; continue; } if (i + 1 >= args.Length) return null; var value = args[++i]; switch (args[i - 1]) { case "--path": path = value; break; case "--state-root": state = value; break; case "--config": config = value; break; case "--stable-id": id = value; break; case "--revision" when long.TryParse(value, out var parsed) && parsed > 0: revision = parsed; break; default: return null; } } return new(path, state, config, revision, id, json, build); }
    [JsonSourceGenerationOptions(UseStringEnumConverter = true)] [JsonSerializable(typeof(PersistedSimilarityClusterRevision))] [JsonSerializable(typeof(ReintroducedCapabilityResult))] private sealed partial class SimilarityJsonContext : JsonSerializerContext;
}
