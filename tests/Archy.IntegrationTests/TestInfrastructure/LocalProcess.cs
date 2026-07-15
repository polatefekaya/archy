using System.Diagnostics;

namespace Archy.IntegrationTests.TestInfrastructure;

internal static class LocalProcess
{
    public static async Task<LocalProcessResult> RunAsync(
        string fileName,
        string workingDirectory,
        string? standardInput,
        params string[] arguments)
        => await RunAsync(fileName, workingDirectory, standardInput, environment: null, arguments);

    public static async Task<LocalProcessResult> RunAsync(
        string fileName,
        string workingDirectory,
        string? standardInput,
        IReadOnlyDictionary<string, string>? environment,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (environment is not null)
        {
            foreach (var variable in environment)
            {
                startInfo.Environment[variable.Key] = variable.Value;
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"'{fileName}' could not start for an integration fixture.");
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput);
            process.StandardInput.Close();
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new LocalProcessResult(process.ExitCode, await standardOutput, await standardError);
    }
}

internal sealed record LocalProcessResult(int ExitCode, string StandardOutput, string StandardError);
