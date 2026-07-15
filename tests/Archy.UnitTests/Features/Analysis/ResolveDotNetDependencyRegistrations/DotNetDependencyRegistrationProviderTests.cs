using Archy.Features.Analysis.ExtractCSharpSyntaxFacts;
using Archy.Features.Analysis.ResolveDotNetDependencyRegistrations;
using Archy.UnitTests.Features.Analysis.ExtractCSharpSyntaxFacts;

namespace Archy.UnitTests.Features.Analysis.ResolveDotNetDependencyRegistrations;

public sealed class DotNetDependencyRegistrationProviderTests
{
    [Fact]
    public async Task ResolveEmitsSeparateExplicitRegistrationEdgesForEachLifetime()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync(
            "src/Registration.cs",
            """
            namespace Sample;

            public interface IClock { }
            public sealed class SystemClock : IClock { }
            public static class Registration
            {
                public static void Configure(object services)
                {
                    services.AddScoped<IClock, SystemClock>();
                    services.AddSingleton<IClock, SystemClock>();
                }
            }
            """);
        var syntax = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [file],
            CancellationToken.None);
        Assert.True(syntax.IsSuccess);

        var result = await new DotNetDependencyRegistrationProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [file],
            syntax.Value.Nodes,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Diagnostics);
        Assert.Equal(2, result.Value.Edges.Count);
        Assert.All(result.Value.Edges, static edge =>
        {
            Assert.Equal("di_registration", edge.EdgeKind);
            Assert.Equal("dotnet-di-syntax", edge.Provider);
            Assert.Equal(0.8, edge.Confidence);
            Assert.Equal("csharp:type:src/Registration.cs:Sample.IClock", edge.SourceStableId);
            Assert.Equal("csharp:type:src/Registration.cs:Sample.SystemClock", edge.TargetStableId);
        });
        Assert.Equal(
            ["AddScoped:IClock=>SystemClock", "AddSingleton:IClock=>SystemClock"],
            result.Value.Edges
                .Select(static edge => edge.NormalizedJoinKey)
                .OrderBy(static joinKey => joinKey, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ResolveReportsAmbiguousTypeNamesInsteadOfGuessingAnEdge()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync(
            "src/Ambiguous.cs",
            """
            namespace First { public interface IClock { } }
            namespace Second { public interface IClock { } }
            namespace Sample
            {
                public sealed class SystemClock { }
                public static class Registration
                {
                    public static void Configure(object services) => services.AddTransient<IClock, SystemClock>();
                }
            }
            """);
        var syntax = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [file],
            CancellationToken.None);
        Assert.True(syntax.IsSuccess);

        var result = await new DotNetDependencyRegistrationProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [file],
            syntax.Value.Nodes,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Edges);
        var diagnostic = Assert.Single(result.Value.Diagnostics);
        Assert.Equal("unresolved_di_registration", diagnostic.Code);
        Assert.Contains("IClock", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveReportsAssemblyScanningPatternsWithoutInventingRegistrationEdges()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync(
            "src/AssemblyScanning.cs",
            """
            namespace Sample;

            public static class Registration
            {
                public static void Configure(object services, object container)
                {
                    services.Scan(scan => scan.FromAssemblyOf<Registration>().AddClasses().AsImplementedInterfaces());
                    container.RegisterAssemblyTypes(Assembly.GetExecutingAssembly());
                }
            }
            """);
        var syntax = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [file],
            CancellationToken.None);
        Assert.True(syntax.IsSuccess);

        var result = await new DotNetDependencyRegistrationProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [file],
            syntax.Value.Nodes,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Edges);
        Assert.Equal(2, result.Value.Diagnostics.Count);
        Assert.All(result.Value.Diagnostics, static diagnostic =>
            Assert.Equal("unsupported_di_registration_pattern", diagnostic.Code));
        Assert.Contains(result.Value.Diagnostics, static diagnostic => diagnostic.Message.Contains("Scan", StringComparison.Ordinal));
        Assert.Contains(result.Value.Diagnostics, static diagnostic => diagnostic.Message.Contains("RegisterAssemblyTypes", StringComparison.Ordinal));
    }
}
