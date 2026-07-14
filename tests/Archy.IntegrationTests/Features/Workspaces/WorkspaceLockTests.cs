using System.Diagnostics;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Workspaces;

public sealed class WorkspaceLockTests
{
    [Fact]
    public async Task AcquireCoordinatesReadAndWriteLocksAcrossProcesses()
    {
        Assert.True(OperatingSystem.IsMacOS(), "The initial lock implementation must be exercised on macOS.");

        using var stateDirectory = TemporaryDirectory.Create("lock");
        var location = CreateLocation(stateDirectory.Path);
        var lockManager = new WorkspaceLockManager(TimeProvider.System);

        var readLock = await lockManager.AcquireAsync(
            location,
            WorkspaceLockMode.Read,
            TimeSpan.Zero,
            CancellationToken.None);
        Assert.True(readLock.IsSuccess);

        using (readLock.Value)
        {
            Assert.Equal("acquired", await RunLockWorkerAsync(stateDirectory.Path, "read", holdMilliseconds: 0));
            Assert.Equal("conflict", await RunLockWorkerAsync(stateDirectory.Path, "write", holdMilliseconds: 0));
        }

        Assert.Equal("acquired", await RunLockWorkerAsync(stateDirectory.Path, "write", holdMilliseconds: 0));

        var workerOutcomePath = Path.Combine(stateDirectory.Path, $"worker-{Guid.NewGuid():N}.txt");
        using var holdingWriter = StartLockWorker(stateDirectory.Path, "write", workerOutcomePath, holdMilliseconds: 750);
        Assert.Equal("acquired", await WaitForWorkerOutcomeAsync(workerOutcomePath));
        Assert.False(holdingWriter.HasExited);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            _ = await lockManager.AcquireAsync(
                location,
                WorkspaceLockMode.Write,
                TimeSpan.FromSeconds(1),
                cancellation.Token);
        });

        await holdingWriter.WaitForExitAsync();
        Assert.Equal(0, holdingWriter.ExitCode);
        Assert.Equal("acquired", await RunLockWorkerAsync(stateDirectory.Path, "write", holdMilliseconds: 0));
    }

    private static WorkspaceStateLocation CreateLocation(string stateDirectory) => new(
        "smoke-test",
        stateDirectory,
        Path.Combine(stateDirectory, "workspace.json"),
        Path.Combine(stateDirectory, "locks", "workspace.lock"),
        Path.Combine(stateDirectory, "archy.db"));

    private static async Task<string> RunLockWorkerAsync(string stateDirectory, string mode, int holdMilliseconds)
    {
        var outcomePath = Path.Combine(stateDirectory, $"worker-{Guid.NewGuid():N}.txt");
        using var process = StartLockWorker(stateDirectory, mode, outcomePath, holdMilliseconds);
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        return await File.ReadAllTextAsync(outcomePath);
    }

    private static Process StartLockWorker(string stateDirectory, string mode, string outcomePath, int holdMilliseconds)
    {
        var hostAssemblyPath = Path.Combine(AppContext.BaseDirectory, "Archy.TestHost.dll");
        Assert.True(File.Exists(hostAssemblyPath), $"The test host '{hostAssemblyPath}' was not copied to the test output.");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(hostAssemblyPath);
        startInfo.ArgumentList.Add("workspace-lock-worker");
        startInfo.ArgumentList.Add(stateDirectory);
        startInfo.ArgumentList.Add(mode);
        startInfo.ArgumentList.Add(outcomePath);
        startInfo.ArgumentList.Add(holdMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Archy could not start the workspace lock test host.");
    }

    private static async Task<string> WaitForWorkerOutcomeAsync(string outcomePath)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            if (File.Exists(outcomePath))
            {
                var outcome = await File.ReadAllTextAsync(outcomePath);
                if (!string.IsNullOrWhiteSpace(outcome))
                {
                    return outcome;
                }
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The workspace lock test host did not report an outcome.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}
