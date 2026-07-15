using Archy.Features.Analysis.CSharpPatternTables;
using Archy.Features.Analysis.MatchCSharpPatternSites;
using Archy.Features.Analysis.ProviderSiteMatches;
using Archy.UnitTests.Features.Analysis.ExtractCSharpSyntaxFacts;

namespace Archy.UnitTests.Features.Analysis.MatchCSharpPatternSites;

public sealed class CSharpPatternSiteMatcherTests
{
    [Fact]
    public async Task MatchEmitsConfiguredInvocationSiteWithLiteralCapture()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync("src/Publisher.cs", """
            namespace Sample;
            public sealed class Publisher
            {
                public void Publish(object bus) => bus.Publish("order.created");
            }
            """);
        var table = new CSharpPatternTable("csharp-pattern-table/v1",
        [new CSharpPatternDefinition("custom-publish", "MyCompany.Messaging", CSharpPatternMatchKind.Invocation, "Publish", null, new CSharpPatternCapture("route", 0))]);

        var result = await new CSharpPatternSiteMatcher().MatchAsync(fixture.RepositoryRoot, [file], table, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var match = Assert.Single(result.Value);
        Assert.Equal(ProviderSiteMatchState.Matched, match.State);
        var capture = Assert.Single(match.Captures);
        Assert.Equal("route", capture.Name);
        Assert.Equal(ProviderSiteCaptureKind.Literal, capture.Kind);
        Assert.Equal("order.created", capture.RawValue);
    }

    [Fact]
    public async Task MatchMarksReceiverConstrainedInvocationAsDegradedRatherThanGuessingItsType()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync("src/Publisher.cs", """
            namespace Sample;
            public sealed class Publisher { public void Publish(object bus) => bus.Publish("order.created"); }
            """);
        var table = new CSharpPatternTable("csharp-pattern-table/v1",
        [new CSharpPatternDefinition("constrained", "MyCompany", CSharpPatternMatchKind.Invocation, "Publish", "MyCompany.IBus", null)]);

        var result = await new CSharpPatternSiteMatcher().MatchAsync(fixture.RepositoryRoot, [file], table, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var match = Assert.Single(result.Value);
        Assert.Equal(ProviderSiteMatchState.Degraded, match.State);
        Assert.Equal("semantic_receiver_required", match.Diagnostic!.Code);
    }
}
