using System.Text;
using Archy.Features.Architecture.ArchitectureExceptions;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Integrations.Codex.GenerateManagedAgentsSection;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Integrations.Codex.PublishAgentsSnapshot;

/// <summary>Publishes only Archy's marker-owned AGENTS.md block after a committed graph revision.</summary>
public sealed class PostRevisionAgentsPublisher(
    IManagedAgentsSectionGenerator generator,
    IArchitectureExceptionPolicyRepository exceptionRepository) : IPostRevisionAgentsPublisher
{
    private const string AgentsFileName = "AGENTS.md";

    public async ValueTask<Result<bool>> PublishAsync(
        string repositoryRoot,
        ArchyConfiguration configuration,
        long graphRevision,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(configuration);
        if (graphRevision < 1)
        {
            return ResultFactory.Failure<bool>(Problem.Validation("A managed AGENTS.md snapshot requires a committed graph revision."));
        }

        var exceptions = await exceptionRepository.ReadAsync(repositoryRoot, cancellationToken);
        if (!exceptions.IsSuccess)
        {
            return ResultFactory.Failure<bool>(exceptions.Problem!);
        }

        var path = Path.Combine(repositoryRoot, AgentsFileName);
        var existing = await ReadUtf8Async(path, cancellationToken);
        if (!existing.IsSuccess)
        {
            return ResultFactory.Failure<bool>(existing.Problem!);
        }

        var generated = generator.Generate(existing.Value!.Text, new AgentsManagedSectionInput(
            graphRevision,
            LayerConventions(configuration),
            ExceptionNotes(exceptions.Value!.Policy),
            ["Open a new Codex task after this snapshot changes; live MCP tools remain the current source of truth."]));
        if (!generated.IsSuccess)
        {
            return ResultFactory.Failure<bool>(generated.Problem!);
        }

        if (!generated.Value!.WasChanged)
        {
            return ResultFactory.Success(false);
        }

        var write = await WriteUtf8AtomicallyAsync(path, generated.Value.Document, existing.Value.HasUtf8Bom, cancellationToken);
        return write.IsSuccess ? ResultFactory.Success(true) : ResultFactory.Failure<bool>(write.Problem!);
    }

    private static string[] LayerConventions(ArchyConfiguration configuration) => configuration.Layers
        .OrderBy(static layer => layer.Name, StringComparer.Ordinal)
        .Select(static layer => $"{layer.Name}: includes {string.Join(", ", layer.Includes)}; may depend on {(layer.MayDependOn.Length == 0 ? "no declared external layer" : string.Join(", ", layer.MayDependOn))}.")
        .ToArray();

    private static string[] ExceptionNotes(ArchitectureExceptionPolicy policy) => policy.Exceptions
        .OrderBy(static item => item.ExpiresAtUtc)
        .ThenBy(static item => item.FindingKey, StringComparer.Ordinal)
        .Select(static item => $"{item.FindingKey}: review {item.ReviewAtUtc:O}; expires {item.ExpiresAtUtc:O}.")
        .ToArray();

    private static async ValueTask<Result<Utf8Document>> ReadUtf8Async(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                return ResultFactory.Success(new Utf8Document(string.Empty, HasUtf8Bom: false));
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var hasBom = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF });
            var offset = hasBom ? 3 : 0;
            return ResultFactory.Success(new Utf8Document(new UTF8Encoding(false, true).GetString(bytes[offset..]), hasBom));
        }
        catch (DecoderFallbackException)
        {
            return ResultFactory.Failure<Utf8Document>(Problem.Validation("AGENTS.md must be valid UTF-8 for Archy to update its managed section."));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<Utf8Document>(Problem.Storage($"Archy could not read AGENTS.md: {exception.Message}"));
        }
    }

    private static async ValueTask<Result<bool>> WriteUtf8AtomicallyAsync(string path, string content, bool includeBom, CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(Path.GetDirectoryName(path)!, $".{AgentsFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            var body = new UTF8Encoding(false, true).GetBytes(content);
            var bytes = includeBom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray() : body;
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
            return ResultFactory.Success(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<bool>(Problem.Storage($"Archy could not publish AGENTS.md: {exception.Message}"));
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A completed atomic write is still valid; an orphaned temporary file can be cleaned later.
            }
        }
    }

    private sealed record Utf8Document(string Text, bool HasUtf8Bom);
}
