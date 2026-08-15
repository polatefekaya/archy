using System.Text.Json;
using System.Text.Json.Serialization;
using Archy.Features.Diagnostics.ReadConfiguredLanguages;
using Archy.Features.Diagnostics.RunDoctor;
using Mediator;

namespace Archy.Features.CommandLine.Diagnostics;

public static partial class DoctorCli
{
    public static async Task<int> RunAsync(string[] args, IMediator mediator, CancellationToken cancellationToken)
    {
        var list = args.Length > 0 && args[0] == "list";
        var options = Parse(list ? args[1..] : args); if (options is null) { Console.Error.WriteLine("Usage: archy doctor [--path <path>] [--config <path>] [--state-root <path>] [--json]\n       archy doctor list [--path <path>] [--config <path>] [--state-root <path>] [--json]"); return 64; }
        if (!list)
        {
            var report = await mediator.Send(new RunDoctorQuery(options.Path, options.Config, options.StateRoot), cancellationToken);
            if (!report.IsSuccess) { Console.Error.WriteLine($"{report.Problem!.Code}: {report.Problem.Message}"); return 2; }
            if (options.Json) Console.WriteLine(JsonSerializer.Serialize(report.Value, DoctorJsonContext.Default.DoctorReport));
            else { Console.WriteLine($"Doctor: {report.Value!.ErrorCount} error(s), {report.Value.WarningCount} warning(s), {report.Value.InfoCount} info."); foreach (var check in report.Value.Checks) Console.WriteLine($"{check.Severity}\t{check.Id}\t{check.Detail}{(check.Remediation is null ? string.Empty : $" Remediation: {check.Remediation}")}"); }
            return report.Value!.HasErrors ? 2 : 0;
        }
        var result = await mediator.Send(new ReadConfiguredLanguagesQuery(options.Path, options.Config, options.StateRoot), cancellationToken); if (!result.IsSuccess) { if (options.Json) Console.WriteLine(JsonSerializer.Serialize(new DoctorError(result.Problem!.Code, result.Problem.Message), DoctorJsonContext.Default.DoctorError)); else Console.Error.WriteLine($"{result.Problem!.Code}: {result.Problem.Message}"); return 2; }
        if (options.Json) { Console.WriteLine(JsonSerializer.Serialize(result.Value, DoctorJsonContext.Default.IReadOnlyListConfiguredLanguageProfileStatus)); return result.Value!.Any(status => status.Readiness is LanguageProfileReadiness.CommandUnavailable or LanguageProfileReadiness.InvalidConfiguration) ? 2 : 0; }
        Console.WriteLine("ID\tLANGUAGE\tCOMMAND\tSOURCES\tMARKERS\tREADINESS"); foreach (var status in result.Value!) Console.WriteLine($"{status.Id}\t{status.LanguageId}\t{status.Command}\t{status.MatchingSourceFileCount}\t{string.Join(',', status.MatchedMarkers)}\t{status.Readiness}"); return result.Value.Any(status => status.Readiness is LanguageProfileReadiness.CommandUnavailable or LanguageProfileReadiness.InvalidConfiguration) ? 2 : 0;
    }
    private static DoctorOptions? Parse(string[] args) { var path = Directory.GetCurrentDirectory(); string? config = null; string? stateRoot = null; var json = false; for (var index = 0; index < args.Length; index++) switch (args[index]) { case "--path" when index + 1 < args.Length: path = args[++index]; break; case "--config" when index + 1 < args.Length: config = args[++index]; break; case "--state-root" when index + 1 < args.Length: stateRoot = args[++index]; break; case "--json": json = true; break; default: return null; } return new(path, config, stateRoot, json); }
    private sealed record DoctorOptions(string Path, string? Config, string? StateRoot, bool Json);
    private sealed record DoctorError(string Code, string Message);
    [JsonSourceGenerationOptions(UseStringEnumConverter = true)] [JsonSerializable(typeof(IReadOnlyList<ConfiguredLanguageProfileStatus>))] [JsonSerializable(typeof(DoctorError))] [JsonSerializable(typeof(DoctorReport))] private sealed partial class DoctorJsonContext : JsonSerializerContext;
}
