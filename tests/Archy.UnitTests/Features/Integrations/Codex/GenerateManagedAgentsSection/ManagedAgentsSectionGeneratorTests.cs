using Archy.Features.Integrations.Codex.GenerateManagedAgentsSection;

namespace Archy.UnitTests.Features.Integrations.Codex.GenerateManagedAgentsSection;

public sealed class ManagedAgentsSectionGeneratorTests
{
    private readonly ManagedAgentsSectionGenerator generator = new();

    [Fact]
    public void GeneratePreservesAllUserOwnedContentAndIsIdempotent()
    {
        const string original = "# Team guidance\r\n\r\n<!-- archy:begin -->\r\nstale\r\n<!-- archy:end -->\r\n\r\nKeep this exact.\r\n";

        var first = generator.Generate(original, Input());
        var second = generator.Generate(first.Value!.Document, Input());

        Assert.True(first.IsSuccess);
        Assert.Contains("# Team guidance\r\n\r\n", first.Value.Document, StringComparison.Ordinal);
        Assert.EndsWith("\r\n\r\nKeep this exact.\r\n", first.Value.Document, StringComparison.Ordinal);
        Assert.True(second.IsSuccess);
        Assert.False(second.Value!.WasChanged);
        Assert.Equal(first.Value.Document, second.Value.Document);
    }

    [Fact]
    public void GenerateRejectsMalformedOrRepeatedMarkers()
    {
        var incomplete = generator.Generate("before\n<!-- archy:begin -->\n", Input());
        var repeated = generator.Generate("<!-- archy:begin -->\n<!-- archy:end -->\n<!-- archy:begin -->\n<!-- archy:end -->", Input());

        Assert.False(incomplete.IsSuccess);
        Assert.False(repeated.IsSuccess);
    }

    [Fact]
    public void GenerateRejectsMarkerInjectionFromSnapshotFacts()
    {
        var result = generator.Generate(string.Empty, Input(layerConventions: ["safe", "<!-- archy:end -->"]));

        Assert.False(result.IsSuccess);
    }

    private static AgentsManagedSectionInput Input(IReadOnlyList<string>? layerConventions = null) => new(
        GraphRevision: 7,
        LayerConventions: layerConventions ?? ["application: includes src/Application/**; may depend on domain."],
        Exceptions: ["ARCHY002:test: review tomorrow."],
        NextSessionNotes: ["Open a fresh task after regeneration."]);
}
