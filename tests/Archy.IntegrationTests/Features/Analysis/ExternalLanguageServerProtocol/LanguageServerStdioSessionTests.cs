using System.Text.Json;
using System.Diagnostics;
using Archy.Features.Analysis.ExternalLanguageServerProtocol;
using Archy.Features.Analysis.ExternalLanguageServerProtocol.EstablishStdioSession;

namespace Archy.IntegrationTests.Features.Analysis.ExternalLanguageServerProtocol;

public sealed class LanguageServerStdioSessionTests
{
    [Fact]
    public async Task MockServerCompletesInitializeConcurrentRequestsAndShutdownOverStdio()
    {
        var repository = Directory.CreateTempSubdirectory("archy-lsp-");
        var specification = new LanguageServerLaunchSpecification(
            LanguageServerProtocolContract.CurrentSchemaVersion,
            "archy-test-host",
            "csharp",
            "dotnet",
            [TestHostAssemblyPath(), "lsp-mock-server"],
            repository.FullName,
            new LanguageServerTimeouts(5_000, 5_000, 5_000),
            new LanguageServerRestartPolicy(0, 0),
            1_048_576,
            65_536,
            [new LanguageServerCapabilityRequirement("definition", true)]);

        try
        {
            var started = await LanguageServerStdioSession.StartAsync(specification, Environment.ProcessId, CancellationToken.None);

            Assert.True(started.IsSuccess, started.IsSuccess ? string.Empty : started.Problem!.Message);
            await using var session = started.Value;
            Assert.Equal("archy-mock-lsp", session.CapabilityProfile.Version.Name);
            Assert.Equal(LanguageServerCapabilityState.Degraded, Assert.Single(session.CapabilityProfile.Capabilities).State);
            using var parameters = JsonDocument.Parse("{\"fixture\":true}");
            var first = session.SendRequestAsync("test/echo", parameters.RootElement.Clone(), CancellationToken.None).AsTask();
            var second = session.SendRequestAsync("test/echo", parameters.RootElement.Clone(), CancellationToken.None).AsTask();
            var responses = await Task.WhenAll(first, second);

            Assert.All(responses, static response => Assert.True(response.IsSuccess, response.IsSuccess ? string.Empty : response.Problem!.Message));
            using (responses[0].Value)
            using (responses[1].Value)
            {
                var ids = responses.Select(static response => response.Value.RootElement.GetProperty("result").GetProperty("echoRequestId").GetInt64()).Order().ToArray();
                Assert.Equal([2L, 3L], ids);
            }

            var shutdown = await session.ShutdownAsync(CancellationToken.None);
            var diagnostics = session.GetDiagnostics();
            Assert.Null(shutdown);
            Assert.Contains("mock-lsp-ready", diagnostics.CapturedStandardError, StringComparison.Ordinal);
            Assert.Contains(diagnostics.Events, static diagnostic => diagnostic.Code == "lsp_server_notification");
        }
        finally
        {
            Directory.Delete(repository.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task CancelledRequestDiscardsItsLateResponseAndKeepsTheSessionUsable()
    {
        var repository = Directory.CreateTempSubdirectory("archy-lsp-cancel-");
        try
        {
            var specification = Specification(repository.FullName, "delay");
            var started = await LanguageServerStdioSession.StartAsync(specification, Environment.ProcessId, CancellationToken.None);

            Assert.True(started.IsSuccess, started.IsSuccess ? string.Empty : started.Problem!.Message);
            await using var session = started.Value;
            using var parameters = JsonDocument.Parse("{}");
            using var cancellation = new CancellationTokenSource(25);
            var canceled = await session.SendRequestAsync("test/delay", parameters.RootElement.Clone(), cancellation.Token);

            Assert.False(canceled.IsSuccess);
            var afterCancellation = await session.SendRequestAsync("test/echo", parameters.RootElement.Clone(), CancellationToken.None);
            Assert.True(afterCancellation.IsSuccess, afterCancellation.IsSuccess ? string.Empty : afterCancellation.Problem!.Message);
            afterCancellation.Value.Dispose();
            Assert.Null(await session.ShutdownAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(repository.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task NativeFixtureNormalizesRelativeMockServerPathsBeforeChangingWorkingDirectory()
    {
        var repository = Directory.CreateTempSubdirectory("archy-lsp-fixture-");
        try
        {
            var testHostAssembly = TestHostAssemblyPath();
            var relativeMockServerPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), testHostAssembly);
            var startInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add(testHostAssembly);
            startInfo.ArgumentList.Add("lsp-client-fixture");
            startInfo.ArgumentList.Add(relativeMockServerPath);
            startInfo.ArgumentList.Add(repository.FullName);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Archy could not start the LSP client fixture.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.Equal(0, process.ExitCode);
            Assert.Contains("native-lsp-client-ok", await standardOutput, StringComparison.Ordinal);
            Assert.Empty(await standardError);
        }
        finally
        {
            Directory.Delete(repository.FullName, recursive: true);
        }
    }

    private static string TestHostAssemblyPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Archy.TestHost.dll");
        Assert.True(File.Exists(path), $"The test host assembly '{path}' was not copied to the test output.");
        return path;
    }

    private static LanguageServerLaunchSpecification Specification(string repositoryRoot, params string[] mockArguments) => new(
        LanguageServerProtocolContract.CurrentSchemaVersion,
        "archy-test-host",
        "csharp",
        "dotnet",
        [TestHostAssemblyPath(), "lsp-mock-server", .. mockArguments],
        repositoryRoot,
        new LanguageServerTimeouts(5_000, 5_000, 5_000),
        new LanguageServerRestartPolicy(0, 0),
        1_048_576,
        65_536,
        [new LanguageServerCapabilityRequirement("definition", true)]);
}
