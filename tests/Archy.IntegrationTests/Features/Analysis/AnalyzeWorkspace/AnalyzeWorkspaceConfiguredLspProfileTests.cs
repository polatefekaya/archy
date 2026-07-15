using Archy.Features.Analysis.AnalyzeWorkspace;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Analysis.AnalyzeWorkspace;

public sealed class AnalyzeWorkspaceConfiguredLspProfileTests
{
    [Fact]
    public async Task AnalyzeExecutesATypescriptProfileDeclaredOnlyInRepositoryConfiguration()
    {
        using var fixture = WorkspaceStateFixture.Create();
        Assert.True((await fixture.InitializeAsync()).IsSuccess);
        var sourcePath = Path.Combine(fixture.Repository.Root, "src", "clock.ts");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "export class Clock {}\n");
        await File.WriteAllTextAsync(Path.Combine(fixture.Repository.Root, "package.json"), "{}");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Repository.Root, "archy.toml"),
            $"""
            schema_version = 1

            [[language_server_profiles]]
            id = "typescript"
            language_id = "typescript"
            extensions = [".ts"]
            markers = ["package.json"]
            command = "dotnet"
            args = ["{TestHostAssemblyPath()}", "lsp-mock-server", "semantic"]
            symbol_identity_prefix = "ts"

            [language_server_profiles.symbol_kinds]
            type = [5]
            """);

        var result = await AnalyzeWorkspaceTestSupport.CreateHandler().Handle(
            new AnalyzeWorkspaceCommand(fixture.Repository.Root, ExplicitConfigurationPath: null, StateRootOverride: fixture.StateRoot),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        var semanticFacts = Assert.Single(result.Value.LanguageServerSemanticFacts);
        Assert.Equal("typescript", semanticFacts.AdapterId);
        Assert.Equal("typescript", semanticFacts.Language);
        Assert.Equal("ts:Clock@src/clock.ts:2:21", Assert.Single(semanticFacts.Symbols).CanonicalId);
        Assert.NotNull(result.Value.GraphRevision);
        Assert.Equal(1, result.Value.GraphRevision.NodeCount);
        Assert.Equal(1, result.Value.GraphRevision.SymbolCount);
    }

    private static string TestHostAssemblyPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Archy.TestHost.dll");
        Assert.True(File.Exists(path), $"The test host assembly was not copied to '{path}'.");
        return path;
    }
}
