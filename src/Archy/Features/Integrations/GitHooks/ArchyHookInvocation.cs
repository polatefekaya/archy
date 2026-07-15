using Archy.SharedKernel.Primitives;

namespace Archy.Features.Integrations.GitHooks;

/// <summary>The exact current Archy process invocation persisted into a POSIX hook without shell interpolation.</summary>
internal sealed record ArchyHookInvocation(string ExecutablePath, string? ManagedAssemblyPath)
{
    public static Result<ArchyHookInvocation> Resolve()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath))
        {
            return ResultFactory.Failure<ArchyHookInvocation>(
                Problem.Conflict("Archy could not determine its executable path for Git hook installation."));
        }

        var processArguments = Environment.GetCommandLineArgs();
        var managedAssemblyArgument = processArguments.FirstOrDefault(static argument =>
            argument.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        string? managedAssemblyPath;
        try
        {
            managedAssemblyPath = managedAssemblyArgument is null
                ? null
                : Path.GetFullPath(managedAssemblyArgument);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ResultFactory.Failure<ArchyHookInvocation>(
                Problem.Conflict($"Archy could not determine its managed entry assembly path for Git hook installation: {exception.Message}"));
        }

        if (managedAssemblyPath is not null && !File.Exists(managedAssemblyPath))
        {
            return ResultFactory.Failure<ArchyHookInvocation>(
                Problem.Conflict("Archy's managed entry assembly no longer exists, so Git hooks cannot safely invoke it."));
        }

        return ResultFactory.Success(new ArchyHookInvocation(executablePath, managedAssemblyPath));
    }
}
