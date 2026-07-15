using System.Text.Json;
using Archy.Features.Analysis.ExtractCSharpSyntaxFacts;
using Archy.Features.Analysis.InventorySources;
using Archy.Features.Analysis.ResolveDotNetConfigurationReads;
using Archy.Features.Analysis.ResolveJsonConfigurationDefinitions;
using Archy.UnitTests.Features.Analysis.ExtractCSharpSyntaxFacts;

namespace Archy.UnitTests.Features.Analysis.ResolveJsonConfigurationDefinitions;

public sealed class JsonConfigurationDefinitionProviderTests
{
    [Fact]
    public async Task ResolveCreatesDefinitionPathsAndClassifiesMissingAndEnvironmentOnlyReads()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var source = await fixture.WriteAsync(
            "src/SettingsReader.cs",
            """
            namespace Sample;
            public sealed class SettingsReader
            {
                public void Read(object configuration)
                {
                    var primary = configuration.GetValue<string>("ConnectionStrings:Primary");
                    var missing = configuration.GetValue<string>("NotConfigured");
                    var environment = Environment.GetEnvironmentVariable("Only__In__Environment");
                }
            }
            """);
        var settings = await fixture.WriteAsync(
            "appsettings.json",
            """
            {
              "ConnectionStrings": { "Primary": "Data Source=archy.db" },
              "Feature": { "Enabled": true },
              "Routes": [{ "Name": "primary" }]
            }
            """,
            SourceLanguage.Unknown);
        var syntax = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [source],
            CancellationToken.None);
        Assert.True(syntax.IsSuccess);
        var reads = await new DotNetConfigurationReadProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [source],
            syntax.Value.Nodes,
            CancellationToken.None);
        Assert.True(reads.IsSuccess);

        var result = await new JsonConfigurationDefinitionProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [source, settings],
            reads.Value.Nodes,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value.Nodes.Count);
        Assert.Contains(result.Value.Nodes, static node => node.StableId == "configuration:file:appsettings.json");
        Assert.Equal(7, result.Value.Edges.Count);
        Assert.Contains(result.Value.Edges, static edge =>
            edge.TargetStableId == "configuration:key:ConnectionStrings:Primary" &&
            edge.EdgeKind == "configuration_defines");
        var primaryEdge = result.Value.Edges.Single(static edge => edge.TargetStableId == "configuration:key:ConnectionStrings:Primary");
        using var evidence = JsonDocument.Parse(primaryEdge.EvidenceJson);
        Assert.Equal("/ConnectionStrings/Primary", evidence.RootElement.GetProperty("jsonPointer").GetString());
        Assert.Contains(result.Value.Diagnostics, static diagnostic => diagnostic.Code == "missing_configuration_definition");
        Assert.Contains(result.Value.Diagnostics, static diagnostic => diagnostic.Code == "environment_only_configuration_key");
    }

    [Fact]
    public async Task ResolveReportsMalformedJsonWithoutCreatingDefinitionEdges()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var settings = await fixture.WriteAsync(
            "appsettings.Development.json",
            """
            { "Feature": { "Enabled": true,, } }
            """,
            SourceLanguage.Unknown);

        var result = await new JsonConfigurationDefinitionProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [settings],
            [],
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var document = Assert.Single(result.Value.Nodes);
        Assert.Equal("configuration_file", document.NodeKind);
        Assert.Empty(result.Value.Edges);
        var diagnostic = Assert.Single(result.Value.Diagnostics);
        Assert.Equal("invalid_configuration_json", diagnostic.Code);
    }

    [Fact]
    public async Task ResolveReportsDuplicateDefinitionsWithoutSelectingOne()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var settings = await fixture.WriteAsync(
            "appsettings.json",
            """
            {
              "Feature": { "First": true },
              "Feature": { "Second": true }
            }
            """,
            SourceLanguage.Unknown);

        var result = await new JsonConfigurationDefinitionProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [settings],
            [],
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(result.Value.Edges, static edge => edge.TargetStableId == "configuration:key:Feature");
        var diagnostic = Assert.Single(result.Value.Diagnostics);
        Assert.Equal("ambiguous_configuration_definition", diagnostic.Code);
    }
}
