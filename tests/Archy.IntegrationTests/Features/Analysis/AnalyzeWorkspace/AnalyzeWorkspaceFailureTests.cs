using Archy.Features.Analysis.AnalyzeWorkspace;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Analysis.AnalyzeWorkspace;

public sealed class AnalyzeWorkspaceFailureTests
{
    [Fact]
    public async Task AnalyzeRecordsAFailedRunWithoutActivatingAPartialGraphForSyntaxErrors()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var sourcePath = Path.Combine(fixture.Repository.Root, "src", "Broken.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "namespace Sample; public sealed class Broken {");

        var result = await AnalyzeWorkspaceTestSupport.CreateHandler().Handle(
            new AnalyzeWorkspaceCommand(fixture.Repository.Root, ExplicitConfigurationPath: null, StateRootOverride: fixture.StateRoot),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsComplete);
        Assert.False(result.Value.WasNoOp);
        Assert.NotNull(result.Value.RunId);
        Assert.NotNull(result.Value.CSharpSyntaxFacts);
        Assert.NotEmpty(result.Value.CSharpSyntaxFacts.Diagnostics);
        Assert.Null(result.Value.GraphRevision);
        await SqliteAssertions.AssertAnalysisRunAsync(
            initialized.Value.StateLocation.DatabasePath,
            result.Value.RunId!,
            expectedStatus: "failed",
            expectedGraphRevision: null,
            expectedEvents: ["started", "failed"]);
        Assert.Equal(0L, await SqliteAssertions.CountAsync(initialized.Value.StateLocation.DatabasePath, "graph_revisions"));
    }
}
