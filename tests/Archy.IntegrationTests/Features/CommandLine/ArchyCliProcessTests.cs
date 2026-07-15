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

    [Fact]
    public async Task DatabaseCheckUsesTheRealHostWithoutInitializingOrChangingState()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        var result = await ArchyProcess.RunAsync(
            "db",
            "check",
            "--path",
            fixture.Repository.Root,
            "--state-root",
            fixture.StateRoot,
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        using var payload = JsonDocument.Parse(result.StandardOutput);
        Assert.True(payload.RootElement.GetProperty("IsHealthy").GetBoolean());
        Assert.Equal(17, payload.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("ActiveGraphRevision").ValueKind);
    }

    [Fact]
    public async Task DatabaseRestoreRequiresAnExplicitBackupAndLeavesStateUntouched()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var originalHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(initialized.Value.StateLocation.DatabasePath)));

        var result = await ArchyProcess.RunAsync(
            "db",
            "restore",
            "--path",
            fixture.Repository.Root,
            "--state-root",
            fixture.StateRoot,
            "--json");

        Assert.Equal(64, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        using var payload = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal("validation", payload.RootElement.GetProperty("Code").GetString());
        Assert.Equal(
            originalHash,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(initialized.Value.StateLocation.DatabasePath))));
    }

    [Fact]
    public async Task InventoryUsesTheRealHostAndReturnsSourceChangesAsJson()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var sourcePath = Path.Combine(fixture.Repository.Root, "src", "InventoryTarget.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "namespace Sample; public sealed class InventoryTarget { }");

        var result = await ArchyProcess.RunAsync(
            "inventory",
            "--path",
            fixture.Repository.Root,
            "--state-root",
            fixture.StateRoot,
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        using var payload = JsonDocument.Parse(result.StandardOutput);
        Assert.True(payload.RootElement.GetProperty("IsComplete").GetBoolean());
        Assert.Contains(
            payload.RootElement.GetProperty("Files").EnumerateArray(),
            static file => file.GetProperty("RepositoryRelativePath").GetString() == "src/InventoryTarget.cs");
        Assert.Contains(
            payload.RootElement.GetProperty("Changes").EnumerateArray(),
            static change => change.GetProperty("Kind").GetString() == "Added" &&
                             change.GetProperty("File").GetProperty("RepositoryRelativePath").GetString() == "src/InventoryTarget.cs");
    }

    [Fact]
    public async Task AnalyzeUsesTheRealHostAndPublishesAnImmutableGraphRevision()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var sourcePath = Path.Combine(fixture.Repository.Root, "src", "AnalyzeTarget.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "namespace Sample; public sealed class AnalyzeTarget { }");

        var result = await ArchyProcess.RunAsync(
            "analyze",
            "--path",
            fixture.Repository.Root,
            "--state-root",
            fixture.StateRoot,
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        using var payload = JsonDocument.Parse(result.StandardOutput);
        Assert.True(payload.RootElement.GetProperty("IsComplete").GetBoolean());
        Assert.False(payload.RootElement.GetProperty("WasNoOp").GetBoolean());
        Assert.Equal(1L, payload.RootElement.GetProperty("GraphRevision").GetProperty("Revision").GetInt64());
        Assert.Equal(1, payload.RootElement.GetProperty("GraphRevision").GetProperty("SymbolCount").GetInt32());
    }

    [Fact]
    public async Task VerifyRunsTheCurrentGraphThroughTheRealHostCompositionRoot()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Repository.Root, "archy.toml"),
            """
            schema_version = 1

            [[layers]]
            name = "All"
            include = ["**"]
            may_depend_on = []
            """);
        var sourcePath = Path.Combine(fixture.Repository.Root, "src", "VerifyTarget.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "namespace Sample; public sealed class VerifyTarget { }");

        var result = await ArchyProcess.RunAsync(
            "verify",
            "--path",
            fixture.Repository.Root,
            "--state-root",
            fixture.StateRoot,
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        using var payload = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal(1L, payload.RootElement.GetProperty("GraphRevision").GetInt64());
        Assert.True(payload.RootElement.GetProperty("IsCompliant").GetBoolean());
        Assert.Empty(payload.RootElement.GetProperty("Evaluation").GetProperty("CoverageIssues").EnumerateArray());
        Assert.Empty(payload.RootElement.GetProperty("Evaluation").GetProperty("Violations").EnumerateArray());
    }

    [Fact]
    public async Task VerifyWritesACompleteSarifReportThroughTheRealHostCompositionRoot()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Repository.Root, "archy.toml"),
            """
            schema_version = 1

            [[layers]]
            name = "All"
            include = ["**"]
            may_depend_on = []
            """);
        var sourcePath = Path.Combine(fixture.Repository.Root, "src", "SarifTarget.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "namespace Sample; public sealed class SarifTarget { }");
        var outputPath = Path.Combine(fixture.Repository.Root, "artifacts", "archy.sarif");

        var result = await ArchyProcess.RunAsync(
            "verify",
            "--path",
            fixture.Repository.Root,
            "--state-root",
            fixture.StateRoot,
            "--sarif",
            "--output",
            outputPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
        Assert.Equal("https://json.schemastore.org/sarif-2.1.0.json", report.RootElement.GetProperty("$schema").GetString());
        Assert.Equal("2.1.0", report.RootElement.GetProperty("version").GetString());
        var run = Assert.Single(report.RootElement.GetProperty("runs").EnumerateArray());
        Assert.Equal("Archy", run.GetProperty("tool").GetProperty("driver").GetProperty("name").GetString());
        Assert.True(run.GetProperty("invocations").EnumerateArray().Single().GetProperty("executionSuccessful").GetBoolean());
        Assert.Empty(run.GetProperty("results").EnumerateArray());
    }

    [Fact]
    public async Task BaselineAcceptWritesTheRepositoryArtifactThroughTheRealHostCompositionRoot()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Repository.Root, "archy.toml"),
            """
            schema_version = 1

            [[layers]]
            name = "All"
            include = ["**"]
            may_depend_on = []
            """);
        var sourcePath = Path.Combine(fixture.Repository.Root, "src", "BaselineTarget.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "namespace Sample; public sealed class BaselineTarget { }");

        var accepted = await ArchyProcess.RunAsync(
            "baseline",
            "accept",
            "--path",
            fixture.Repository.Root,
            "--state-root",
            fixture.StateRoot,
            "--json");

        Assert.Equal(0, accepted.ExitCode);
        Assert.Equal(string.Empty, accepted.StandardError);
        using var acceptedPayload = JsonDocument.Parse(accepted.StandardOutput);
        Assert.Equal(1L, acceptedPayload.RootElement.GetProperty("GraphRevision").GetInt64());
        Assert.Equal(0, acceptedPayload.RootElement.GetProperty("FindingCount").GetInt32());
        var baselinePath = Path.Combine(fixture.Repository.Root, "archy.baseline.json");
        Assert.True(File.Exists(baselinePath));

        var verified = await ArchyProcess.RunAsync(
            "verify",
            "--path",
            fixture.Repository.Root,
            "--state-root",
            fixture.StateRoot,
            "--json");

        Assert.Equal(0, verified.ExitCode);
        Assert.Equal(string.Empty, verified.StandardError);
        using var verificationPayload = JsonDocument.Parse(verified.StandardOutput);
        Assert.Equal("Compatible", verificationPayload.RootElement.GetProperty("Baseline").GetProperty("Status").GetString());
        Assert.Empty(verificationPayload.RootElement.GetProperty("Baseline").GetProperty("Findings").EnumerateArray());
    }
}
