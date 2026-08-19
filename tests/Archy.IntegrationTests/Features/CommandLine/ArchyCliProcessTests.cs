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
    public async Task DoctorAndLanguageInventoryAreReadOnlyAndReturnStructuredReadiness()
    {
        using var fixture = TemporaryRepository.Create();
        var stateRoot = Path.Combine(Path.GetTempPath(), $"archy-doctor-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "archy.toml"), """
                schema_version = 1

                [[language_server_profiles]]
                id = "typescript"
                language_id = "typescript"
                extensions = [".ts"]
                markers = ["package.json"]
                command = "typescript-language-server"
                args = ["--stdio"]
                symbol_identity_prefix = "ts"
                max_symbol_queries = 7

                [language_server_profiles.symbol_kinds]
                namespace = [3]
                type = [5]
                method = [6]
                property = [7]
                field = [8]
                event = [24]
                parameter = [26]

                [scope]
                include = ["src/**/*.ts"]
                """);
            Directory.CreateDirectory(Path.Combine(fixture.Root, "src"));
            Directory.CreateDirectory(Path.Combine(fixture.Root, "node_modules", "dependency"));
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "package.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "src", "App.ts"), "export const app = true;");
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "node_modules", "dependency", "index.ts"), "export const dependency = true;");
            var report = await ArchyProcess.RunAsync("doctor", "--path", fixture.Root, "--state-root", stateRoot, "--json");
            var inventory = await ArchyProcess.RunAsync("doctor", "list", "--path", fixture.Root, "--state-root", stateRoot, "--json");

            Assert.Equal(2, report.ExitCode);
            Assert.Equal(2, inventory.ExitCode);
            Assert.Equal(string.Empty, report.StandardError);
            Assert.Equal(string.Empty, inventory.StandardError);
            Assert.False(Directory.Exists(stateRoot));
            using var reportPayload = JsonDocument.Parse(report.StandardOutput);
            Assert.Contains(reportPayload.RootElement.GetProperty("Checks").EnumerateArray(), check => check.GetProperty("Id").GetString() == "workspace.database");
            Assert.Contains(reportPayload.RootElement.GetProperty("Checks").EnumerateArray(), check => check.GetProperty("Id").GetString() == "embeddings.credential" && !check.GetProperty("Detail").GetString()!.Contains("OPENAI_API_KEY=", StringComparison.Ordinal));
            using var inventoryPayload = JsonDocument.Parse(inventory.StandardOutput);
            Assert.Contains(inventoryPayload.RootElement.EnumerateArray(), profile => profile.GetProperty("Id").GetString() == "csharp");
            var typescript = Assert.Single(inventoryPayload.RootElement.EnumerateArray(), profile => profile.GetProperty("Id").GetString() == "typescript");
            Assert.Equal(1, typescript.GetProperty("MatchingSourceFileCount").GetInt32());
            Assert.Contains("package.json", typescript.GetProperty("MatchedMarkers").EnumerateArray().Select(static marker => marker.GetString()));
        }
        finally
        {
            if (Directory.Exists(stateRoot)) Directory.Delete(stateRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DoctorDoesNotMutateAnInitializedWorkspaceDatabase()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var before = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(initialized.Value.StateLocation.DatabasePath)));
        var result = await ArchyProcess.RunAsync("doctor", "--path", fixture.Repository.Root, "--state-root", fixture.StateRoot, "--json");
        var after = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(initialized.Value.StateLocation.DatabasePath)));
        Assert.Equal(0, result.ExitCode); Assert.Equal(before, after); Assert.Equal(string.Empty, result.StandardError);
        using var payload = JsonDocument.Parse(result.StandardOutput); Assert.Contains(payload.RootElement.GetProperty("Checks").EnumerateArray(), check => check.GetProperty("Id").GetString() == "workspace.database" && check.GetProperty("Severity").GetString() == "Info");
    }

    [Fact]
    public async Task PlanningCommandsDispatchThroughTheRealHostWithJsonContracts()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var node = GraphRevisionTestBuilder.Node("node:planning", "aaaaaaaaaaaaaaaa") with { DisplayName = "PlanTarget", CanonicalKey = "Planning.PlanTarget" };
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [node]);
        var explain = await ArchyProcess.RunAsync("architecture", "explain", "--lookup", node.StableId, "--path", fixture.Repository.Root, "--state-root", fixture.StateRoot, "--json");
        var impact = await ArchyProcess.RunAsync("impact", "analyze", "--target", node.StableId, "--path", fixture.Repository.Root, "--state-root", fixture.StateRoot, "--json");
        var summary = await ArchyProcess.RunAsync("change", "summary", "--base-revision", "1", "--path", fixture.Repository.Root, "--state-root", fixture.StateRoot, "--json");

        Assert.Equal(0, explain.ExitCode); Assert.Equal(0, impact.ExitCode); Assert.Equal(0, summary.ExitCode);
        using var explainJson = JsonDocument.Parse(explain.StandardOutput); Assert.Equal(node.StableId, explainJson.RootElement.GetProperty("ResolvedStableId").GetString());
        using var impactJson = JsonDocument.Parse(impact.StandardOutput); Assert.Equal(node.StableId, impactJson.RootElement.GetProperty("TargetStableId").GetString());
        using var summaryJson = JsonDocument.Parse(summary.StandardOutput); Assert.Equal(1, summaryJson.RootElement.GetProperty("BaseRevision").GetInt64());
    }

    [Fact]
    public async Task SimilarityReadCommandsDispatchThroughTheRealHostWithoutCreatingClusters()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var node = GraphRevisionTestBuilder.Node("node:similarity", "aaaaaaaaaaaaaaaa") with { DisplayName = "CreateSession", CanonicalKey = "Sessions.CreateSession", FilePath = "src/Sessions.cs" };
        var peer = GraphRevisionTestBuilder.Node("node:similarity-peer", "bbbbbbbbbbbbbbbb") with { DisplayName = "CreateSession", CanonicalKey = "Sessions.CreateSession", FilePath = "src/Sessions.cs" };
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [node, peer]);
        var clusters = await ArchyProcess.RunAsync("similarity", "clusters", "--path", fixture.Repository.Root, "--state-root", fixture.StateRoot, "--json");
        var build = await ArchyProcess.RunAsync("similarity", "clusters", "--build", "--path", fixture.Repository.Root, "--state-root", fixture.StateRoot, "--json");
        var reintroduced = await ArchyProcess.RunAsync("similarity", "reintroduced", "--stable-id", node.StableId, "--path", fixture.Repository.Root, "--state-root", fixture.StateRoot, "--json");
        Assert.Equal(0, clusters.ExitCode); Assert.Equal("null", clusters.StandardOutput.Trim()); Assert.Equal(0, build.ExitCode); using var buildPayload = JsonDocument.Parse(build.StandardOutput); Assert.NotEmpty(buildPayload.RootElement.GetProperty("Clusters").EnumerateArray());
        Assert.Equal(0, reintroduced.ExitCode); using var payload = JsonDocument.Parse(reintroduced.StandardOutput); Assert.True(payload.RootElement.GetProperty("Abstained").GetBoolean());
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
        Assert.Equal(19, payload.RootElement.GetProperty("SchemaVersion").GetInt32());
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
    public async Task VerifyBlocksWhenTwoPopulatedLayersHaveNoEnforceableEdge()
    {
        // Syntax-only analysis records `using` and `declares` edges, neither of which is
        // confidence-1.0 enforceable. Two layers own code, a rule forbids the direction between
        // them, and the gate still has nothing to test. That must read as a blocked gate, not a
        // clean repository.
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Repository.Root, "archy.toml"),
            """
            schema_version = 1

            [[layers]]
            name = "Api"
            include = ["src/Api/**"]
            may_depend_on = ["Domain"]

            [[layers]]
            name = "Domain"
            include = ["src/Domain/**"]
            may_depend_on = []

            [enforcement]
            hard_edge_kinds = ["calls", "references", "inherits"]
            """);
        var apiPath = Path.Combine(fixture.Repository.Root, "src", "Api", "OrderEndpoint.cs");
        var domainPath = Path.Combine(fixture.Repository.Root, "src", "Domain", "Order.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(apiPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(domainPath)!);
        await File.WriteAllTextAsync(apiPath, "namespace Shop.Api; public sealed class OrderEndpoint { }");
        await File.WriteAllTextAsync(domainPath, "using Shop.Api;\n\nnamespace Shop.Domain; public sealed class Order { }");

        var result = await ArchyProcess.RunAsync(
            "verify",
            "--path",
            fixture.Repository.Root,
            "--state-root",
            fixture.StateRoot,
            "--json");

        Assert.Equal(1, result.ExitCode);
        using var payload = JsonDocument.Parse(result.StandardOutput);
        Assert.False(payload.RootElement.GetProperty("IsCompliant").GetBoolean());
        var reach = payload.RootElement.GetProperty("Evaluation").GetProperty("Reach");
        Assert.True(reach.GetProperty("IsUnenforceable").GetBoolean());
        Assert.Equal(0, reach.GetProperty("EligibleEdgeCount").GetInt32());
        Assert.Equal(2, reach.GetProperty("PopulatedLayerCount").GetInt32());
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
