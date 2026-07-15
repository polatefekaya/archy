using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.CSharpPatternTables;

public sealed class CSharpPatternTableLoader : ICSharpPatternTableLoader
{
    public const string SchemaVersion = "csharp-pattern-table/v1";

    private static readonly CSharpPatternDefinition[] BuiltInPatterns =
    [
        new("dotnet-di-add-scoped", "Microsoft.Extensions.DependencyInjection", CSharpPatternMatchKind.Invocation, "AddScoped", null, null),
        new("dotnet-di-add-singleton", "Microsoft.Extensions.DependencyInjection", CSharpPatternMatchKind.Invocation, "AddSingleton", null, null),
        new("dotnet-di-add-transient", "Microsoft.Extensions.DependencyInjection", CSharpPatternMatchKind.Invocation, "AddTransient", null, null),
        new("dotnet-message-publish", "typed-messaging", CSharpPatternMatchKind.Invocation, "Publish", null, new("message", 0)),
        new("dotnet-message-send", "typed-messaging", CSharpPatternMatchKind.Invocation, "Send", null, new("message", 0)),
    ];

    public Result<CSharpPatternTable> Load(ArchyConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var definitions = new List<CSharpPatternDefinition>(BuiltInPatterns);
        var identifiers = new HashSet<string>(BuiltInPatterns.Select(static pattern => pattern.Id), StringComparer.Ordinal);
        foreach (var pattern in configuration.ProviderPatterns.OrderBy(static pattern => pattern.Id, StringComparer.Ordinal))
        {
            var parsed = Parse(pattern);
            if (!parsed.IsSuccess)
            {
                return ResultFactory.Failure<CSharpPatternTable>(parsed.Problem!);
            }

            if (!identifiers.Add(parsed.Value.Id))
            {
                return ResultFactory.Failure<CSharpPatternTable>(
                    Problem.Validation($"Provider pattern '{parsed.Value.Id}' duplicates a built-in or earlier configured pattern."));
            }

            definitions.Add(parsed.Value);
        }

        return ResultFactory.Success(new CSharpPatternTable(
            SchemaVersion,
            [.. definitions.OrderBy(static pattern => pattern.Id, StringComparer.Ordinal)]));
    }

    private static Result<CSharpPatternDefinition> Parse(ProviderPatternConfiguration pattern)
    {
        if (!Enum.TryParse<CSharpPatternMatchKind>(pattern.MatchKind, ignoreCase: true, out var matchKind))
        {
            return ResultFactory.Failure<CSharpPatternDefinition>(Problem.Validation($"Provider pattern '{pattern.Id}' has unsupported match kind '{pattern.MatchKind}'."));
        }

        if ((pattern.CaptureName is null) != (pattern.CaptureArgumentIndex is null))
        {
            return ResultFactory.Failure<CSharpPatternDefinition>(Problem.Validation($"Provider pattern '{pattern.Id}' must define capture_name and capture_argument_index together."));
        }

        if (matchKind != CSharpPatternMatchKind.Invocation && pattern.CaptureName is not null)
        {
            return ResultFactory.Failure<CSharpPatternDefinition>(Problem.Validation($"Provider pattern '{pattern.Id}' may capture an argument only for invocation matching."));
        }

        return ResultFactory.Success(new CSharpPatternDefinition(
            pattern.Id,
            pattern.Framework,
            matchKind,
            pattern.Member,
            pattern.Type,
            pattern.CaptureName is null ? null : new CSharpPatternCapture(pattern.CaptureName, pattern.CaptureArgumentIndex!.Value)));
    }
}
