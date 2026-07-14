using System.Diagnostics;

namespace Archy.IntegrationTests.TestInfrastructure;

internal static class ArchyProcess
{
    public static async Task<ArchyProcessResult> RunAsync(params string[] arguments)
    {
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, "Archy.dll");
        Assert.True(File.Exists(assemblyPath), $"The Archy host assembly '{assemblyPath}' was not copied to the test output.");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(assemblyPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Archy could not start its CLI process.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ArchyProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }
}

internal sealed record ArchyProcessResult(int ExitCode, string StandardOutput, string StandardError);
