using System.Diagnostics;

namespace Archy.IntegrationTests.TestInfrastructure;

internal static class GitProcess
{
    public static async Task<GitProcessResult> RunAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Git could not start for an integration fixture.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new GitProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    public static async Task RequireSuccessAsync(string workingDirectory, params string[] arguments)
    {
        var result = await RunAsync(workingDirectory, arguments);
        Assert.True(
            result.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed with exit code {result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}.{Environment.NewLine}{result.StandardError}");
    }
}

internal sealed record GitProcessResult(int ExitCode, string StandardOutput, string StandardError);
