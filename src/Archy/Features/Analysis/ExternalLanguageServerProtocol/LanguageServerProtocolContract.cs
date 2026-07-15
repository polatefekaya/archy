using Archy.Features.Analysis.LanguageSemanticAdapters;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ExternalLanguageServerProtocol;

public static class LanguageServerProtocolContract
{
    public const string CurrentSchemaVersion = "lsp-process/v1";
    public const string JsonRpcVersion = "2.0";
    public const int InitializeRequestId = 1;
    public const string ContentLengthHeaderName = "Content-Length";
    public const string HeaderTerminator = "\r\n\r\n";
    public const string TextEncoding = "utf-8";

    public static Problem? ValidateLaunchSpecification(LanguageServerLaunchSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);

        if (!string.Equals(specification.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(specification.ServerId) ||
            string.IsNullOrWhiteSpace(specification.LanguageId) ||
            string.IsNullOrWhiteSpace(specification.Command) ||
            string.IsNullOrWhiteSpace(specification.RepositoryRoot) ||
            !Path.IsPathFullyQualified(specification.RepositoryRoot) ||
            specification.Arguments is null ||
            specification.Arguments.Any(static argument => argument is null || argument.Contains('\0')) ||
            specification.Timeouts is null ||
            !ValidTimeouts(specification.Timeouts) ||
            specification.RestartPolicy is null ||
            specification.RestartPolicy.MaximumRestarts is < 0 or > 3 ||
            specification.RestartPolicy.InitialBackoffMilliseconds is < 0 or > 30_000 ||
            specification.MaximumMessageBytes is < 1 or > 64 * 1024 * 1024 ||
            specification.MaximumStandardErrorBytes is < 1 or > 4 * 1024 * 1024 ||
            specification.RequiredCapabilities is null ||
            specification.RequiredCapabilities.Any(static requirement => string.IsNullOrWhiteSpace(requirement.Name)) ||
            specification.RequiredCapabilities.Select(static requirement => requirement.Name).Distinct(StringComparer.Ordinal).Count() != specification.RequiredCapabilities.Count)
        {
            return Problem.Validation("Language-server launch specifications require a supported schema, absolute repository root, process-safe arguments, bounded timeouts and capture limits, restart policy, and uniquely named capabilities.");
        }

        return null;
    }

    public static Result<LspInitializeRequest> CreateInitializeRequest(LanguageServerLaunchSpecification specification, int? clientProcessId)
    {
        var validation = ValidateLaunchSpecification(specification);
        if (validation is not null)
        {
            return ResultFactory.Failure<LspInitializeRequest>(validation);
        }

        if (clientProcessId is < 1)
        {
            return ResultFactory.Failure<LspInitializeRequest>(Problem.Validation("Language-server initialize requests require either no client process ID or a positive process ID."));
        }

        var rootUri = DirectoryUri(specification.RepositoryRoot);
        return ResultFactory.Success(new LspInitializeRequest(
            JsonRpcVersion,
            InitializeRequestId,
            "initialize",
            new LspInitializeParameters(
                clientProcessId,
                rootUri,
                [new LspWorkspaceFolder(rootUri, Path.GetFileName(Path.TrimEndingDirectorySeparator(specification.RepositoryRoot)))],
                new LspInitializeClientCapabilities(
                    new LspWorkspaceClientCapabilities(WorkspaceFolders: true),
                    new LspTextDocumentClientCapabilities(
                        new LspRequestClientCapability(DynamicRegistration: false),
                        new LspRequestClientCapability(DynamicRegistration: false),
                        new LspRequestClientCapability(DynamicRegistration: false),
                        new LspRequestClientCapability(DynamicRegistration: false),
                        new LspRequestClientCapability(DynamicRegistration: false))),
                new LspClientInfo("Archy", "1"),
                new ArchyLspInitializationOptions(
                    CurrentSchemaVersion,
                    SemanticAnalysisContract.CurrentSchemaVersion,
                    RequiresSnapshotBoundResults: true))));
    }

    private static bool ValidTimeouts(LanguageServerTimeouts timeouts) =>
        timeouts.InitializeMilliseconds is >= 100 and <= 120_000 &&
        timeouts.RequestMilliseconds is >= 100 and <= 120_000 &&
        timeouts.ShutdownMilliseconds is >= 100 and <= 120_000;

    private static string DirectoryUri(string repositoryRoot)
    {
        var fullPath = Path.GetFullPath(repositoryRoot);
        var directoryPath = Path.EndsInDirectorySeparator(fullPath)
            ? fullPath
            : string.Concat(fullPath, Path.DirectorySeparatorChar);
        return new Uri(directoryPath).AbsoluteUri;
    }
}
