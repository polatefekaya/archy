using Archy.Features.Analysis.InventorySources;
using Archy.Features.Configuration.LoadEffectiveConfiguration;

namespace Archy.IntegrationTests.Features.Analysis.InventorySources;

public sealed class InitialSourceInventoryTests
{
    [Fact]
    public async Task InitialInventoryUsesScopeRulesGitIgnoreAndBuiltInExclusions()
    {
        using var fixture = await SourceInventoryFixture.CreateAsync();
        await fixture.WriteRepositoryFileAsync(
            ".gitignore",
            """
            ignored/*
            !ignored/Included.cs
            *.secret
            """);
        await fixture.WriteRepositoryFileAsync("src/Application.cs", "namespace Sample; public sealed class Application { }");
        await fixture.WriteRepositoryFileAsync("src/Notes.md", "notes");
        await fixture.WriteRepositoryFileAsync("ignored/Included.cs", "namespace Sample; public sealed class Included { }");
        await fixture.WriteRepositoryFileAsync("ignored/Excluded.cs", "namespace Sample; public sealed class Excluded { }");
        await fixture.WriteRepositoryFileAsync("diagnostics.secret", "do-not-inventory");
        await fixture.WriteRepositoryFileAsync("bin/Generated.cs", "namespace Sample; public sealed class Generated { }");
        await fixture.WriteRepositoryFileAsync("obj/Generated.cs", "namespace Sample; public sealed class Generated { }");
        await fixture.WriteRepositoryFileAsync("generated/Generated.cs", "namespace Sample; public sealed class Generated { }");
        await fixture.WriteRepositoryFileAsync(".git/config", "metadata");
        var configuration = ArchyConfiguration.Default with
        {
            Scope = new ScopeConfiguration([], ["generated/**"]),
        };

        var result = await fixture.SynchronizeAsync(configuration);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsComplete);
        Assert.Equal(
            [".gitignore", "ignored/Included.cs", "src/Application.cs", "src/Notes.md"],
            result.Value.Files.Select(static file => file.RepositoryRelativePath));
        Assert.All(result.Value.Changes, static change => Assert.Equal(SourceFileChangeKind.Added, change.Kind));
        Assert.Equal(
            ["ignored/Included.cs", "src/Application.cs"],
            result.Value.ParseCandidates.Select(static file => file.RepositoryRelativePath));
        Assert.Contains(
            result.Value.Exclusions,
            static exclusion => exclusion is { RepositoryRelativePath: ".git", Reason: SourcePathExclusionReason.GitMetadata });
        Assert.Contains(
            result.Value.Exclusions,
            static exclusion => exclusion is { RepositoryRelativePath: "bin", Reason: SourcePathExclusionReason.GeneratedDirectory });
        Assert.Contains(
            result.Value.Exclusions,
            static exclusion => exclusion is { RepositoryRelativePath: "obj", Reason: SourcePathExclusionReason.GeneratedDirectory });
        Assert.Contains(
            result.Value.Exclusions,
            static exclusion => exclusion is { RepositoryRelativePath: "generated", Reason: SourcePathExclusionReason.ScopeExclude });
        Assert.Contains(
            result.Value.Exclusions,
            static exclusion => exclusion is { RepositoryRelativePath: "diagnostics.secret", Reason: SourcePathExclusionReason.GitIgnore });
        Assert.Contains(
            result.Value.Exclusions,
            static exclusion => exclusion is { RepositoryRelativePath: "ignored/Excluded.cs", Reason: SourcePathExclusionReason.GitIgnore });
        Assert.Empty(result.Value.Diagnostics);
    }
}
