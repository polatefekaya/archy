using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;

return args is ["workspace-lock-worker", ..]
    ? await WorkspaceLockWorker.RunAsync(args[1..])
    : 64;

internal static class WorkspaceLockWorker
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 4 ||
            !Enum.TryParse<WorkspaceLockMode>(args[1], ignoreCase: true, out var mode) ||
            !int.TryParse(args[3], out var holdMilliseconds) ||
            holdMilliseconds < 0)
        {
            return 64;
        }

        var stateDirectory = args[0];
        var outcomePath = args[2];
        var location = new WorkspaceStateLocation(
            "lock-worker",
            stateDirectory,
            Path.Combine(stateDirectory, "workspace.json"),
            Path.Combine(stateDirectory, "locks", "workspace.lock"),
            Path.Combine(stateDirectory, "archy.db"));
        var result = await new WorkspaceLockManager(TimeProvider.System).AcquireAsync(
            location,
            mode,
            TimeSpan.FromMilliseconds(250),
            CancellationToken.None);

        await File.WriteAllTextAsync(outcomePath, result.IsSuccess ? "acquired" : result.Problem!.Code);
        if (!result.IsSuccess)
        {
            return 0;
        }

        using var lease = result.Value;
        if (holdMilliseconds > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(holdMilliseconds));
        }

        return 0;
    }
}
