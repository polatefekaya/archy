using Archy.Features.Duplicates.SelectEmbeddingChunks;
using Archy.Features.Memory.DetermineImportantNodes;

namespace Archy.UnitTests.Features.Duplicates.SelectEmbeddingChunks;

public sealed class CSharpEmbeddingChunkSelectorTests
{
    [Fact]
    public void CreateChunksIncludesOnlyTheTargetMethodSignatureDocumentationAndBody()
    {
        const string source = """
            using System;
            namespace Sample;
            public sealed class Clock
            {
                /// <summary>Gets a clock value.</summary>
                public int GetTick() { return 42; }
                public int Ignore() { return 99; }
            }
            """;
        var eligibility = new ImportantNodeEligibility(1, [new ImportantNodeTarget(ImportantNodeTargetKind.PublicApi, "method:tick", "GetTick", ["src/Clock.cs"], [ImportantNodeEligibilityReason.PublicApiDeclaration])], []);

        var chunks = new CSharpEmbeddingChunkSelector().CreateChunks(eligibility, [new EmbeddingChunkSource("method:tick", "src/Clock.cs", 6, 6, source)]);

        var chunk = Assert.Single(chunks);
        Assert.Contains("GetTick", chunk.Content, StringComparison.Ordinal);
        Assert.Contains("Gets a clock value", chunk.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("using System", chunk.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Ignore", chunk.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateChunksIncludesAnEligiblePublicTypeWhenMethodFactsAreUnavailable()
    {
        const string source = """
            namespace Sample;
            /// <summary>Coordinates architecture sessions.</summary>
            public sealed class SessionCoordinator
            {
                public string Create(string name) => name.Trim();
            }
            """;
        var eligibility = new ImportantNodeEligibility(1, [new ImportantNodeTarget(ImportantNodeTargetKind.PublicApi, "type:SessionCoordinator", "SessionCoordinator", ["src/SessionCoordinator.cs"], [ImportantNodeEligibilityReason.PublicApiDeclaration])], []);

        var chunks = new CSharpEmbeddingChunkSelector().CreateChunks(eligibility, [new EmbeddingChunkSource("type:SessionCoordinator", "src/SessionCoordinator.cs", 3, 6, source)]);

        var chunk = Assert.Single(chunks);
        Assert.Contains("ClassDeclaration", chunk.Content, StringComparison.Ordinal);
        Assert.Contains("Coordinates architecture sessions", chunk.Content, StringComparison.Ordinal);
        Assert.Contains("SessionCoordinator", chunk.Content, StringComparison.Ordinal);
        Assert.Contains("Create", chunk.Content, StringComparison.Ordinal);
    }
}
