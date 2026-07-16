using Archy.Features.Architecture.ArchitectureExceptions;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Integrations.Codex.GenerateManagedAgentsSection;
using Archy.Features.Integrations.Codex.PublishAgentsSnapshot;
using Archy.SharedKernel.Primitives;

namespace Archy.UnitTests.Features.Integrations.Codex.PublishAgentsSnapshot;

public sealed class PostRevisionAgentsPublisherTests
{
    [Fact]
    public async Task PublishReplacesOnlyTheManagedBlockAndSkipsAnIdenticalRevision()
    {
        var directory = Directory.CreateTempSubdirectory("archy-agents-test-");
        try
        {
            var path = Path.Combine(directory.FullName, "AGENTS.md");
            await File.WriteAllTextAsync(path, "# User guidance\n\n<!-- archy:begin -->\nold\n<!-- archy:end -->\n\nKeep this.\n");
            var publisher = new PostRevisionAgentsPublisher(new ManagedAgentsSectionGenerator(), new EmptyExceptionRepository());

            var first = await publisher.PublishAsync(directory.FullName, Configuration(), 9, CancellationToken.None);
            var document = await File.ReadAllTextAsync(path);
            var second = await publisher.PublishAsync(directory.FullName, Configuration(), 9, CancellationToken.None);

            Assert.True(first.IsSuccess);
            Assert.True(first.Value);
            Assert.Contains("# User guidance", document, StringComparison.Ordinal);
            Assert.EndsWith("\n\nKeep this.\n", document, StringComparison.Ordinal);
            Assert.Contains("Graph revision: 9", document, StringComparison.Ordinal);
            Assert.True(second.IsSuccess);
            Assert.False(second.Value);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static ArchyConfiguration Configuration() => ArchyConfiguration.Default with
    {
        Layers = [new LayerRuleConfiguration("application", ["src/Application/**"], ["domain"])],
    };

    private sealed class EmptyExceptionRepository : IArchitectureExceptionPolicyRepository
    {
        public ValueTask<Result<ArchitectureExceptionPolicyReadResult>> ReadAsync(string repositoryRoot, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ResultFactory.Success(new ArchitectureExceptionPolicyReadResult(
                Path.Combine(repositoryRoot, ArchitectureExceptionPolicyRepository.FileName),
                new ArchitectureExceptionPolicy(1, []))));

        public ValueTask<Result<string>> AppendAsync(string repositoryRoot, ArchitectureExceptionDecision architectureException, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ResultFactory.Failure<string>(Problem.Conflict("Not used by this test.")));
    }
}
