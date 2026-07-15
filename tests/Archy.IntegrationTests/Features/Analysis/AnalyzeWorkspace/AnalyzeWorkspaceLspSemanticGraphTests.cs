using Archy.Features.Analysis.AnalyzeLanguageServerSemantics;
using Archy.Features.Analysis.AnalyzeWorkspace;
using Archy.Features.Analysis.LanguageSemanticAdapters;
using Archy.IntegrationTests.TestInfrastructure;
using Archy.SharedKernel.Primitives;

namespace Archy.IntegrationTests.Features.Analysis.AnalyzeWorkspace;

public sealed class AnalyzeWorkspaceLspSemanticGraphTests
{
    [Fact]
    public async Task AnalyzeCommitsOnlyTheCompleteCsharpSemanticBatchAlongsideSyntaxFacts()
    {
        using var fixture = WorkspaceStateFixture.Create();
        Assert.True((await fixture.InitializeAsync()).IsSuccess);
        var projectPath = Path.Combine(fixture.Repository.Root, "Archy.Sample.csproj");
        var sourcePath = Path.Combine(fixture.Repository.Root, "src", "Order.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await File.WriteAllTextAsync(sourcePath, "namespace Sample; public sealed class Order { }");
        var semanticAnalyzer = new CompleteSemanticAnalyzer();

        var result = await AnalyzeWorkspaceTestSupport.CreateHandler(semanticAnalyzer).Handle(
            new AnalyzeWorkspaceCommand(fixture.Repository.Root, ExplicitConfigurationPath: null, StateRootOverride: fixture.StateRoot),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        var semanticFacts = Assert.Single(result.Value.LanguageServerSemanticFacts);
        Assert.Single(semanticFacts.Symbols);
        Assert.NotNull(result.Value.GraphRevision);
        Assert.Equal(result.Value.CSharpSyntaxFacts!.Nodes.Count + 1, result.Value.GraphRevision.NodeCount);
        Assert.Equal(result.Value.CSharpSyntaxFacts.Symbols.Count + 1, result.Value.GraphRevision.SymbolCount);
        Assert.Equal(1, semanticAnalyzer.Calls);
    }

    private sealed class CompleteSemanticAnalyzer : IConfiguredLspSemanticAnalyzer
    {
        public int Calls { get; private set; }

        public ValueTask<Result<SemanticAnalysisResult>> AnalyzeAsync(ConfiguredLspSemanticAnalysisRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            var result = new SemanticAnalysisResult(
                SemanticAnalysisContract.CurrentSchemaVersion,
                "roslyn",
                "csharp",
                request.SemanticRequest.SnapshotId,
                [new SemanticCapability("documentSymbol", SemanticCapabilityState.Available, null)],
                [new SemanticSymbol("csharp:Sample.Order@src/Order.cs:1:39", "Order", SemanticSymbolKind.Type, null, "public", new SemanticSourceRange("src/Order.cs", 1, 39, 1, 44))],
                [], [], [], [], [], [], []);
            return ValueTask.FromResult(ResultFactory.Success(result));
        }
    }
}
