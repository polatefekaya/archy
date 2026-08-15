using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Graph.TraverseDependencies;
using Archy.Features.Integrations.Codex.ComposeChangeSummary;
using Archy.Features.Planning.AnalyzeImpact;
using Archy.Features.Planning.ExplainArchitecture;
using Archy.Features.Planning.PlanChange;
using Archy.Features.Planning.PlanSafeRefactor;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.ResolveWorkspaceState;
using Mediator;

namespace Archy.Features.CommandLine.Planning;

/// <summary>Read-only planning CLI adapters. They resolve existing workspace state but never initialize or analyze it.</summary>
public static partial class PlanningCli
{
    public static async Task<int> RunAsync(string root, string[] args, IMediator mediator, CancellationToken ct)
    {
        if (args.Length == 0) return Usage(); var options = Options.Parse(args[1..]); if (options is null) return Usage();
        var state = await mediator.Send(new ResolveWorkspaceStateQuery(options.Path, options.Config, options.StateRoot), ct); if (!state.IsSuccess) return Error(options.Json, state.Problem!.Message);
        var locks = new WorkspaceLockManager(TimeProvider.System); var snapshots = new GraphRevisionSnapshotReader(locks); var impact = new ImpactAnalyzer(new GraphTraversalReader(locks), snapshots);
        return (root, args[0]) switch
        {
            ("architecture", "explain") when !string.IsNullOrWhiteSpace(options.Value) => await Explain(state.Value, options, snapshots, locks, ct),
            ("change", "plan") when !string.IsNullOrWhiteSpace(options.Value) => await Change(state.Value, options, snapshots, impact, ct),
            ("change", "summary") when options.BaseRevision is not null => await Summary(state.Value, options, locks, ct),
            ("impact", "analyze") when !string.IsNullOrWhiteSpace(options.Value) => await Impact(state.Value, options, impact, ct),
            ("refactor", "plan") when !string.IsNullOrWhiteSpace(options.Value) && ParseIntent(options.Intent) is { } intent => await Refactor(state.Value, options, snapshots, impact, intent, ct),
            _ => Usage()
        };
    }
    private static async Task<int> Explain(Archy.Features.Workspaces.InitializeWorkspace.WorkspaceStateLocation state, Options o, GraphRevisionSnapshotReader snapshots, WorkspaceLockManager locks, CancellationToken ct) { var r = await new ArchitectureExplainer(snapshots, new Archy.Features.Decisions.ArchitectureDecisions.ArchitectureDecisionRepository(TimeProvider.System, locks)).ExplainAsync(state, new(o.Value!, o.Depth), ct); return Write(o.Json, r, PlanningJsonContext.Default.ArchitectureExplanation); }
    private static async Task<int> Change(Archy.Features.Workspaces.InitializeWorkspace.WorkspaceStateLocation state, Options o, GraphRevisionSnapshotReader snapshots, ImpactAnalyzer impact, CancellationToken ct) { var r = await new ChangePlanner(snapshots, impact).PlanAsync(state, new(o.Value!, o.Files, o.Target, null, o.Snippet), ct); return Write(o.Json, r, PlanningJsonContext.Default.ChangePlan); }
    private static async Task<int> Summary(Archy.Features.Workspaces.InitializeWorkspace.WorkspaceStateLocation state, Options o, WorkspaceLockManager locks, CancellationToken ct) { var r = await new ArchitectureChangeSummaryComposer(locks).ComposeAsync(state, o.BaseRevision!.Value, ct); return Write(o.Json, r, PlanningJsonContext.Default.ArchitectureChangeSummary); }
    private static async Task<int> Impact(Archy.Features.Workspaces.InitializeWorkspace.WorkspaceStateLocation state, Options o, ImpactAnalyzer impact, CancellationToken ct) { var r = await impact.AnalyzeAsync(state, new(o.Value!, ImpactDirection.Both, o.Depth, o.MaxNodes), ct); return Write(o.Json, r, PlanningJsonContext.Default.ImpactAnalysisResult); }
    private static async Task<int> Refactor(Archy.Features.Workspaces.InitializeWorkspace.WorkspaceStateLocation state, Options o, GraphRevisionSnapshotReader snapshots, ImpactAnalyzer impact, RefactorIntent intent, CancellationToken ct) { var r = await new SafeRefactorPlanner(snapshots, impact).PlanAsync(state, new(o.Value!, intent, o.Destination), ct); return Write(o.Json, r, PlanningJsonContext.Default.SafeRefactorPlan); }
    private static int Write<T>(bool json, Archy.SharedKernel.Primitives.Result<T> result, JsonTypeInfo<T> info) { if (!result.IsSuccess) return Error(json, result.Problem!.Message); if (json) Console.WriteLine(JsonSerializer.Serialize(result.Value, info)); else Console.WriteLine(JsonSerializer.Serialize(result.Value, info)); return 0; }
    private static int Error(bool json, string message) { Console.Error.WriteLine(message); return 2; }
    private static int Usage() { Console.Error.WriteLine("Usage: archy architecture explain --lookup <id|path|text> [--depth <0..20>] [--path <path>] [--state-root <path>] [--config <path>] [--json]\n       archy change plan --description <text> [--file <relative-path>] [--target <stable-id>] [--snippet <text>] [options]\n       archy change summary --base-revision <n> [options]\n       archy impact analyze --target <stable-id> [--depth <1..8>] [--max-nodes <1..500>] [options]\n       archy refactor plan --target <stable-id> --intent <move|split|merge|rename|extract|replace> [--destination <relative-path>] [options]"); return 64; }
    private static RefactorIntent? ParseIntent(string? value) => value?.ToLowerInvariant() switch { "move" => RefactorIntent.Move, "split" => RefactorIntent.Split, "merge" => RefactorIntent.Merge, "rename" => RefactorIntent.Rename, "extract" => RefactorIntent.Extract, "replace" => RefactorIntent.Replace, _ => null };
    private sealed record Options(string Path, string? StateRoot, string? Config, bool Json, string? Value, string? Target, string? Intent, string? Destination, string? Snippet, IReadOnlyList<string> Files, int Depth, int MaxNodes, long? BaseRevision)
    { public static Options? Parse(string[] args) { var path = Directory.GetCurrentDirectory(); string? state = null, config = null, value = null, target = null, intent = null, destination = null, snippet = null; long? baseRevision = null; var json = false; var files = new List<string>(); var depth = 3; var max = 100; for (var i = 0; i < args.Length; i++) { var key = args[i]; if (key == "--json") { json = true; continue; } if (i + 1 >= args.Length) return null; var v = args[++i]; switch (key) { case "--path": path = v; break; case "--state-root": state = v; break; case "--config": config = v; break; case "--lookup": case "--description": value = v; break; case "--target": target = v; if (value is null) value = v; break; case "--intent": intent = v; break; case "--destination": destination = v; break; case "--snippet": snippet = v; break; case "--file": files.Add(v); break; case "--depth" when int.TryParse(v, out var d): depth = d; break; case "--max-nodes" when int.TryParse(v, out var n): max = n; break; case "--base-revision" when long.TryParse(v, out var revision) && revision > 0: baseRevision = revision; break; default: return null; } } return new(path, state, config, json, value, target, intent, destination, snippet, files, depth, max, baseRevision); } }
    [JsonSourceGenerationOptions(UseStringEnumConverter = true)] [JsonSerializable(typeof(ArchitectureExplanation))] [JsonSerializable(typeof(ChangePlan))] [JsonSerializable(typeof(ImpactAnalysisResult))] [JsonSerializable(typeof(SafeRefactorPlan))] [JsonSerializable(typeof(ArchitectureChangeSummary))] private sealed partial class PlanningJsonContext : JsonSerializerContext;
}
