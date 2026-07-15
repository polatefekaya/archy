using System.Text.Json;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Integrations.ContinuousIntegration;

public sealed class ProviderNeutralCiEntrypointTests
{
    [Fact]
    public async Task UsesATrustedBinaryToEmitSarifAndPropagatesTheIntroducedViolationExitCode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await using var repository = await GitRepositoryFixture.CreateAsync();
        await repository.WriteAsync(
            "archy.toml",
            """
            schema_version = 1

            [[layers]]
            name = "Inside"
            include = ["src/Inside/**"]
            may_depend_on = []
            """);
        await repository.WriteAsync("src/Inside/Inside.cs", "namespace Sample; public sealed class Inside { }");
        await repository.StageAllAsync();
        await repository.CommitAsync("CI architecture fixture", bypassHooks: true);

        var binary = await CreateHostWrapperAsync(repository.Root);
        var reportPath = Path.Combine(repository.Root, "artifacts", "archy.sarif");
        var stateRoot = Path.Combine(repository.Root, ".ci-state");
        var environment = new Dictionary<string, string>
        {
            ["ARCHY_BIN"] = binary,
            ["ARCHY_REPOSITORY_ROOT"] = repository.Root,
            ["ARCHY_SARIF_OUTPUT"] = reportPath,
            ["ARCHY_STATE_ROOT"] = stateRoot,
        };
        var entrypoint = Path.Combine(SourceRepository.Root, "scripts", "ci", "verify.sh");

        var passed = await LocalProcess.RunAsync(
            "sh",
            repository.Root,
            standardInput: null,
            environment,
            entrypoint);

        Assert.True(
            passed.ExitCode == 0,
            $"CI entrypoint failed.{Environment.NewLine}stdout:{Environment.NewLine}{passed.StandardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{passed.StandardError}");
        Assert.Contains(reportPath, passed.StandardOutput, StringComparison.Ordinal);
        using (var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath)))
        {
            Assert.Equal("2.1.0", report.RootElement.GetProperty("version").GetString());
            Assert.Empty(report.RootElement.GetProperty("runs").EnumerateArray().Single().GetProperty("results").EnumerateArray());
        }

        await repository.WriteAsync("src/Outside/Outside.cs", "namespace Sample; public sealed class Outside { }");
        var blocked = await LocalProcess.RunAsync(
            "sh",
            repository.Root,
            standardInput: null,
            environment,
            entrypoint);

        Assert.Equal(1, blocked.ExitCode);
        using var blockedReport = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
        Assert.Contains(
            blockedReport.RootElement.GetProperty("runs").EnumerateArray().Single().GetProperty("results").EnumerateArray(),
            result => result.GetProperty("level").GetString() == "error" &&
                      result.GetProperty("baselineState").GetString() == "new");
    }

    private static async Task<string> CreateHostWrapperAsync(string repositoryRoot)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The provider-neutral CI entrypoint currently targets POSIX hosts.");
        }

        var dotnet = Environment.ProcessPath;
        Assert.False(string.IsNullOrWhiteSpace(dotnet));
        var assembly = Path.Combine(AppContext.BaseDirectory, "Archy.dll");
        Assert.True(File.Exists(assembly));
        var wrapper = Path.Combine(repositoryRoot, "tools", "archy-ci-test-host");
        Directory.CreateDirectory(Path.GetDirectoryName(wrapper)!);
        await File.WriteAllTextAsync(
            wrapper,
            $"#!/bin/sh{Environment.NewLine}exec '{dotnet!.Replace("'", "'\\\"'\\\"'", StringComparison.Ordinal)}' '{assembly.Replace("'", "'\\\"'\\\"'", StringComparison.Ordinal)}' \"$@\"{Environment.NewLine}");
        File.SetUnixFileMode(
            wrapper,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute |
            UnixFileMode.GroupRead |
            UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead |
            UnixFileMode.OtherExecute);
        return wrapper;
    }
}
