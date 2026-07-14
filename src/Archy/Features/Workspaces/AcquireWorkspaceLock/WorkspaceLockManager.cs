using System.Runtime.InteropServices;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Workspaces.AcquireWorkspaceLock;

public sealed class WorkspaceLockManager(TimeProvider timeProvider) : IWorkspaceLockManager
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(50);

    public async ValueTask<Result<IWorkspaceLockLease>> AcquireAsync(
        WorkspaceStateLocation location,
        WorkspaceLockMode mode,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        cancellationToken.ThrowIfCancellationRequested();
        if (timeout < TimeSpan.Zero)
        {
            return ResultFactory.Failure<IWorkspaceLockLease>(
                Problem.Validation("Workspace lock timeout cannot be negative."));
        }

        if (!OperatingSystem.IsMacOS())
        {
            return ResultFactory.Failure<IWorkspaceLockLease>(
                Problem.Conflict("Workspace locking is currently supported on macOS only."));
        }

        FileStream stream;
        var leaseOwnsStream = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(location.LockPath)!);
            stream = new FileStream(
                location.LockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<IWorkspaceLockLease>(
                Problem.Storage($"Archy could not open workspace lock '{location.LockPath}': {exception.Message}"));
        }

        try
        {
            var deadline = timeProvider.GetUtcNow() + timeout;
            while (true)
            {
                var attempt = TryAcquire(stream, mode);
                if (attempt.Kind == LockAttemptKind.Acquired)
                {
                    leaseOwnsStream = true;
                    return ResultFactory.Success<IWorkspaceLockLease>(
                        new WorkspaceLockLease(stream, mode));
                }

                if (attempt.Kind == LockAttemptKind.Failed)
                {
                    return ResultFactory.Failure<IWorkspaceLockLease>(attempt.Problem!);
                }

                var remaining = deadline - timeProvider.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    return ResultFactory.Failure<IWorkspaceLockLease>(
                        Problem.Conflict(
                            $"Timed out waiting {timeout.TotalMilliseconds:0}ms for the {mode.ToString().ToLowerInvariant()} workspace lock."));
                }

                var delay = remaining < RetryInterval ? remaining : RetryInterval;
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
        }
        finally
        {
            if (!leaseOwnsStream)
            {
                stream.Dispose();
            }
        }
    }

    private static LockAttempt TryAcquire(FileStream stream, WorkspaceLockMode mode)
    {
        var operation = mode == WorkspaceLockMode.Read
            ? MacOsFlockNative.SharedNonBlocking
            : MacOsFlockNative.ExclusiveNonBlocking;
        var result = MacOsFlockNative.Flock(
            stream.SafeFileHandle.DangerousGetHandle().ToInt32(),
            operation);
        if (result == 0)
        {
            return LockAttempt.Acquired;
        }

        var error = Marshal.GetLastPInvokeError();
        return error is MacOsFlockNative.Interrupted or MacOsFlockNative.WouldBlock
            ? LockAttempt.Busy
            : LockAttempt.Failed(
                Problem.Storage($"Archy could not acquire the workspace lock: macOS error {error}."));
    }

    private enum LockAttemptKind
    {
        Acquired,
        Busy,
        Failed,
    }

    private readonly record struct LockAttempt(LockAttemptKind Kind, Problem? Problem)
    {
        public static LockAttempt Acquired { get; } = new(LockAttemptKind.Acquired, null);

        public static LockAttempt Busy { get; } = new(LockAttemptKind.Busy, null);

        public static LockAttempt Failed(Problem problem) => new(LockAttemptKind.Failed, problem);
    }

    private sealed class WorkspaceLockLease(FileStream stream, WorkspaceLockMode mode) : IWorkspaceLockLease
    {
        private int _disposed;

        public WorkspaceLockMode Mode { get; } = mode;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _ = MacOsFlockNative.Flock(
                stream.SafeFileHandle.DangerousGetHandle().ToInt32(),
                MacOsFlockNative.Unlock);
            stream.Dispose();
        }
    }

    private static class MacOsFlockNative
    {
        internal const int SharedNonBlocking = 1 | 4;
        internal const int ExclusiveNonBlocking = 2 | 4;
        internal const int Unlock = 8;
        internal const int Interrupted = 4;
        internal const int WouldBlock = 35;

        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "flock", SetLastError = true)]
        internal static extern int Flock(int fileDescriptor, int operation);
    }
}
