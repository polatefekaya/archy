using System.Text.Json;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Integrations.GitHubActions;

public sealed class GitHubActionsTemplateContractTests
{
    [Fact]
    public async Task TemplateAndCompositeActionPreserveTheRequiredEnforcementAndSarifContract()
    {
        var root = SourceRepository.Root;
        var action = await File.ReadAllTextAsync(Path.Combine(root, "actions", "archy-verify", "action.yml"));
        var workflow = await File.ReadAllTextAsync(Path.Combine(root, ".github", "workflow-templates", "archy-verify.yml"));
        var guidance = await File.ReadAllTextAsync(Path.Combine(root, "docs", "ci", "github-actions.md"));
        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflow-templates", "archy-verify.properties.json")));

        Assert.Contains("using: composite", action, StringComparison.Ordinal);
        Assert.Contains("ARCHY_SHA256", action, StringComparison.Ordinal);
        Assert.Contains("ARCHY_STATE_ROOT", action, StringComparison.Ordinal);
        Assert.Contains("scripts/ci/verify.sh", action, StringComparison.Ordinal);
        Assert.Contains("sarif-path", action, StringComparison.Ordinal);

        Assert.Contains("name: Archy / verify", workflow, StringComparison.Ordinal);
        Assert.Contains("runs-on: macos-14", workflow, StringComparison.Ordinal);
        Assert.Contains("security-events: write", workflow, StringComparison.Ordinal);
        Assert.Contains("uses: polatefekaya/archy/actions/archy-verify@v0.1.0", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@v4", workflow, StringComparison.Ordinal);
        Assert.Contains("github/codeql-action/upload-sarif@v4", workflow, StringComparison.Ordinal);
        Assert.Contains("continue-on-error: true", workflow, StringComparison.Ordinal);
        Assert.Contains("steps.archy.outcome == 'failure'", workflow, StringComparison.Ordinal);

        Assert.Equal("Archy verification", metadata.RootElement.GetProperty("name").GetString());
        Assert.Contains("Baseline and exception governance", guidance, StringComparison.Ordinal);
        Assert.Contains("Branch-protection checklist", guidance, StringComparison.Ordinal);
        Assert.Contains("pull_request_target", guidance, StringComparison.Ordinal);
    }
}
