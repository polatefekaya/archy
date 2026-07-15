using Archy.Features.Analysis.ExtractCSharpSyntaxFacts;
using Archy.Features.Analysis.ResolveDotNetDependencyConsumptions;
using Archy.Features.Analysis.ResolveDotNetDependencyRegistrations;
using Archy.UnitTests.Features.Analysis.ExtractCSharpSyntaxFacts;

namespace Archy.UnitTests.Features.Analysis.ResolveDotNetDependencyConsumptions;

public sealed class DotNetDependencyConsumptionProviderTests
{
    [Fact]
    public async Task ResolveEmitsConsumerToServiceForAnExplicitlyRegisteredConstructorParameter()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync(
            "src/Registration.cs",
            """
            namespace Sample;

            public interface IClock { }
            public sealed class SystemClock : IClock { }
            public sealed class TimeReporter
            {
                public TimeReporter(IClock clock) { }
            }

            public static class Registration
            {
                public static void Configure(object services) => services.AddScoped<IClock, SystemClock>();
            }
            """);
        var syntax = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [file],
            CancellationToken.None);
        Assert.True(syntax.IsSuccess);
        var registrations = await new DotNetDependencyRegistrationProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [file],
            syntax.Value.Nodes,
            CancellationToken.None);
        Assert.True(registrations.IsSuccess);

        var result = await new DotNetDependencyConsumptionProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [file],
            syntax.Value.Nodes,
            registrations.Value.Edges,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Diagnostics);
        var edge = Assert.Single(result.Value.Edges);
        Assert.Equal("di_consumes", edge.EdgeKind);
        Assert.Equal("dotnet-di-consumption-syntax", edge.Provider);
        Assert.Equal(0.8, edge.Confidence);
        Assert.Equal("IClock", edge.NormalizedJoinKey);
        Assert.Equal("csharp:type:src/Registration.cs:Sample.TimeReporter", edge.SourceStableId);
        Assert.Equal("csharp:type:src/Registration.cs:Sample.IClock", edge.TargetStableId);
    }

    [Fact]
    public async Task ResolveRetainsPrimaryConstructorServiceFactAndReportsMultipleImplementations()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync(
            "src/MultipleRegistrations.cs",
            """
            namespace Sample;

            public interface IClock { }
            public sealed class SystemClock : IClock { }
            public sealed class FrozenClock : IClock { }
            public sealed class TimeReporter(IClock clock) { }
            public static class Registration
            {
                public static void Configure(object services)
                {
                    services.AddSingleton<IClock, SystemClock>();
                    services.AddSingleton<IClock, FrozenClock>();
                }
            }
            """);
        var syntax = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [file],
            CancellationToken.None);
        Assert.True(syntax.IsSuccess);
        var registrations = await new DotNetDependencyRegistrationProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [file],
            syntax.Value.Nodes,
            CancellationToken.None);
        Assert.True(registrations.IsSuccess);

        var result = await new DotNetDependencyConsumptionProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [file],
            syntax.Value.Nodes,
            registrations.Value.Edges,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var edge = Assert.Single(result.Value.Edges);
        Assert.Equal("csharp:type:src/MultipleRegistrations.cs:Sample.TimeReporter", edge.SourceStableId);
        Assert.Equal("csharp:type:src/MultipleRegistrations.cs:Sample.IClock", edge.TargetStableId);
        var diagnostic = Assert.Single(result.Value.Diagnostics);
        Assert.Equal("ambiguous_di_registration", diagnostic.Code);
        Assert.Contains("2 explicit registrations", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveDoesNotCreateAnUnregisteredConstructorDependencyEdge()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync(
            "src/Unregistered.cs",
            """
            namespace Sample;

            public interface IClock { }
            public sealed class TimeReporter
            {
                public TimeReporter(IClock clock) { }
            }
            """);
        var syntax = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [file],
            CancellationToken.None);
        Assert.True(syntax.IsSuccess);

        var result = await new DotNetDependencyConsumptionProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [file],
            syntax.Value.Nodes,
            [],
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Edges);
        var diagnostic = Assert.Single(result.Value.Diagnostics);
        Assert.Equal("unregistered_constructor_dependency", diagnostic.Code);
        Assert.Contains("IClock", diagnostic.Message, StringComparison.Ordinal);
    }
}
