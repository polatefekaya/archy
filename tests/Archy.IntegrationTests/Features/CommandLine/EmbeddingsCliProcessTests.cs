using System.Text.Json;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.CommandLine;

public sealed class EmbeddingsCliProcessTests
{
    [Fact]
    public async Task IndexDryRunThenDeterministicIndexAndStatusReturnMachineReadableResults()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var sourcePath = Path.Combine(fixture.Repository.Root, "src", "Sessions.cs"); Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "namespace Sample;\npublic sealed class Sessions\n{\n    public void Create()\n    {\n        _ = 1;\n    }\n}\n");
        await File.WriteAllTextAsync(Path.Combine(fixture.Repository.Root, "archy.toml"), """
schema_version = 1

[model]
provider = "openai"
embedding_model = "test-model"

[memory]
source_sharing = "summaries_and_embeddings"
""");
        var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess, initialized.Problem?.Message);
        var node = new GraphNodeFact("method:Create", "method", "method:Create", "Create", "src/Sessions.cs", 4, 7, "test", 1, "{}", new string('a', 64));
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [node], symbols: [new("symbol:create", node.StableId, "Sample.Sessions.Create", "public", "Create()", "[]", "{}", "hash")]);
        var common = new[] { "--path", fixture.Repository.Root, "--state-root", fixture.StateRoot, "--json" };
        var dryRun = await ArchyProcess.RunAsync(["embeddings", "index", "--dry-run", .. common]);
        Assert.Equal(0, dryRun.ExitCode); using (var dryJson = JsonDocument.Parse(dryRun.StandardOutput)) { Assert.Equal(1, dryJson.RootElement.GetProperty("RequestedChunks").GetInt32()); Assert.Equal(0, dryJson.RootElement.GetProperty("Generated").GetInt32()); }
        var environment = new Dictionary<string, string> { ["ARCHY_TEST_EMBEDDING_PROVIDER"] = "deterministic" };
        var indexed = await ArchyProcess.RunAsync(environment, "embeddings", "index", "--path", fixture.Repository.Root, "--state-root", fixture.StateRoot, "--json"); Assert.Equal(0, indexed.ExitCode);
        var status = await ArchyProcess.RunAsync("embeddings", "status", "--path", fixture.Repository.Root, "--state-root", fixture.StateRoot, "--json"); Assert.Equal(0, status.ExitCode); Assert.DoesNotContain("\"Vector\":", status.StandardOutput, StringComparison.Ordinal);
        using var statusJson = JsonDocument.Parse(status.StandardOutput); Assert.Equal(1, statusJson.RootElement.GetProperty("CachedVectorCount").GetInt32());
    }
}
