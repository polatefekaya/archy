using Archy.Features.CommandLine.Embeddings;

namespace Archy.UnitTests.Features.CommandLine.Embeddings;

public sealed class ConfigureEmbeddingSetupTests
{
    [Fact]
    public void ApplyAddsOnlyRequiredSectionsToAnExistingConfiguration()
    {
        var updated = ConfigureEmbeddingSetup.Apply("schema_version = 1\n\n[scope]\ninclude = [\"src/**/*.cs\"]\n", "openai", "text-embedding-3-large");

        Assert.Contains("[model]", updated, StringComparison.Ordinal);
        Assert.StartsWith("schema_version = 1", updated, StringComparison.Ordinal);
        Assert.Contains("embedding_model = \"text-embedding-3-large\"", updated, StringComparison.Ordinal);
        Assert.Contains("[memory]", updated, StringComparison.Ordinal);
        Assert.Contains("source_sharing = \"summaries_and_embeddings\"", updated, StringComparison.Ordinal);
        Assert.Contains("[scope]", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyUpdatesExistingKeysWithoutDuplicatingThem()
    {
        var updated = ConfigureEmbeddingSetup.Apply("[model]\nprovider = \"disabled\"\nembedding_model = \"old\"\n\n[memory]\nsource_sharing = \"disabled\"\n", "openai", "text-embedding-3-small");

        Assert.Equal(1, updated.Split("embedding_model", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, updated.Split("source_sharing", StringSplitOptions.None).Length - 1);
        Assert.Contains("provider = \"openai\"", updated, StringComparison.Ordinal);
        Assert.Contains("embedding_model = \"text-embedding-3-small\"", updated, StringComparison.Ordinal);
    }
}
