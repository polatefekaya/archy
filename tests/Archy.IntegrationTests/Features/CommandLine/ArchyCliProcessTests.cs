using System.Text.Json;
using Archy.Features.CommandLine;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.CommandLine;

public sealed class ArchyCliProcessTests
{
    [Fact]
    public async Task VersionReportsProductMetadataFromTheRealHostProcess()
    {
        var result = await ArchyProcess.RunAsync("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"Archy {ArchyProductMetadata.Version}{Environment.NewLine}", result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
    }

    [Fact]
    public async Task WorkspaceLocateDispatchesThroughTheRealHostCompositionRoot()
    {
        using var fixture = TemporaryRepository.Create();

        var result = await ArchyProcess.RunAsync(
            "workspace",
            "locate",
            "--path",
            fixture.Root,
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);

        using var payload = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal(fixture.Root, payload.RootElement.GetProperty("RepositoryRoot").GetString());
        Assert.False(payload.RootElement.GetProperty("IsLinkedWorktree").GetBoolean());
    }
}
