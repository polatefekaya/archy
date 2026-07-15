using Archy.Features.Analysis.ExtractCSharpSyntaxFacts;
using Archy.Features.Analysis.ResolveDotNetConfigurationReads;
using Archy.UnitTests.Features.Analysis.ExtractCSharpSyntaxFacts;

namespace Archy.UnitTests.Features.Analysis.ResolveDotNetConfigurationReads;

public sealed class DotNetConfigurationReadProviderTests
{
    [Fact]
    public async Task ResolveCreatesStableKeyNodesForIndexerValueBindingAndEnvironmentReads()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync(
            "src/SettingsReader.cs",
            """
            namespace Sample;

            public sealed class SettingsReader
            {
                public void Read(object configuration)
                {
                    var connection = configuration["ConnectionStrings:Primary"];
                    var level = configuration.GetSection("Logging").GetValue<string>("Level", "Information");
                    configuration.GetSection("Feature").Bind(this);
                    var apiKey = System.Environment.GetEnvironmentVariable("Feature__ApiKey");
                }
            }
            """);
        var syntax = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [file],
            CancellationToken.None);
        Assert.True(syntax.IsSuccess);

        var result = await new DotNetConfigurationReadProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [file],
            syntax.Value.Nodes,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Diagnostics);
        Assert.Equal(4, result.Value.Nodes.Count);
        Assert.All(result.Value.Nodes, static node =>
        {
            Assert.Equal("configuration_key", node.NodeKind);
            Assert.Equal("dotnet-configuration-syntax", node.Provider);
            Assert.Equal(0.8, node.Confidence);
            Assert.Null(node.FilePath);
        });
        Assert.Equal(
            ["ConnectionStrings:Primary", "Feature", "Feature:ApiKey", "Logging:Level"],
            result.Value.Nodes.Select(static node => node.DisplayName).OrderBy(static key => key, StringComparer.Ordinal));
        Assert.Equal(
            [
                "bind:Feature",
                "environment:Feature:ApiKey",
                "get_value:Logging:Level",
                "indexer:ConnectionStrings:Primary",
            ],
            result.Value.Edges.Select(static edge => edge.NormalizedJoinKey).OrderBy(static joinKey => joinKey, StringComparer.Ordinal));
        Assert.All(result.Value.Edges, static edge =>
        {
            Assert.Equal("configuration_read", edge.EdgeKind);
            Assert.Equal("csharp:type:src/SettingsReader.cs:Sample.SettingsReader", edge.SourceStableId);
            Assert.Equal("dotnet-configuration-syntax", edge.Provider);
        });
    }

    [Fact]
    public async Task ResolveReportsDynamicKeysAndDoesNotGuessConfigurationFacts()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync(
            "src/DynamicSettingsReader.cs",
            """
            namespace Sample;

            public sealed class DynamicSettingsReader
            {
                public void Read(object configuration, string key)
                {
                    var indexed = configuration[key];
                    var valued = configuration.GetValue<string>(key);
                    var environment = Environment.GetEnvironmentVariable(key);
                }
            }
            """);
        var syntax = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [file],
            CancellationToken.None);
        Assert.True(syntax.IsSuccess);

        var result = await new DotNetConfigurationReadProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [file],
            syntax.Value.Nodes,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Nodes);
        Assert.Empty(result.Value.Edges);
        Assert.Equal(3, result.Value.Diagnostics.Count);
        Assert.All(result.Value.Diagnostics, static diagnostic =>
            Assert.Equal("dynamic_configuration_key", diagnostic.Code));
    }

    [Fact]
    public async Task ResolveDoesNotTreatAnArbitraryGetValueMethodAsAConfigurationRead()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync(
            "src/NotConfiguration.cs",
            """
            namespace Sample;

            public sealed class NotConfiguration
            {
                public string GetValue<T>(string name) => name;
                public string Read() => GetValue<string>("DisplayName");
            }
            """);
        var syntax = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [file],
            CancellationToken.None);
        Assert.True(syntax.IsSuccess);

        var result = await new DotNetConfigurationReadProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [file],
            syntax.Value.Nodes,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Nodes);
        Assert.Empty(result.Value.Edges);
        Assert.Empty(result.Value.Diagnostics);
    }
}
