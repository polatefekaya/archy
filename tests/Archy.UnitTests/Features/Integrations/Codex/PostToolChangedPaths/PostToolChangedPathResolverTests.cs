using Archy.Features.Analysis.InventorySources;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Integrations.Codex.PostToolChangedPaths;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.Features.Workspaces.ReadRepositoryCommit;
using Archy.SharedKernel.Primitives;

namespace Archy.UnitTests.Features.Integrations.Codex.PostToolChangedPaths;

public sealed class PostToolChangedPathResolverTests
{
    [Fact]
    public async Task ResolveCombinesHookSnapshotAndGitEvidenceWithoutTreatingNoOpsOrGeneratedFilesAsCode()
    {
        var root = Path.Combine(Path.GetTempPath(), $"archy-path-resolution-{Guid.NewGuid():N}");
        var state = Path.Combine(Path.GetTempPath(), $"archy-path-resolution-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(state);
        try
        {
            var scope = await SourceScopePolicy.CreateAsync(root, state, ArchyConfiguration.Default.Scope, CancellationToken.None);
            Assert.True(scope.IsSuccess, scope.IsSuccess ? string.Empty : scope.Problem!.Message);
            var request = new PostToolChangedPathRequest(
                new LocatedWorkspace(root, Path.Combine(root, ".git"), IsLinkedWorktree: false),
                scope.Value,
                PostToolKind.Bash,
                ["src/Code.cs", "src/Generated.g.cs", "assets/Blob.bin", "src/NoOp.cs"],
                new WorkspacePathSnapshot(
                [
                    Entry("src/Code.cs", false, "before-code"),
                    Entry("src/Generated.g.cs", false, "before-generated"),
                    Entry("assets/Blob.bin", true, "before-binary"),
                    Entry("src/NoOp.cs", false, "same"),
                ]),
                new WorkspacePathSnapshot(
                [
                    Entry("src/Code.cs", false, "after-code"),
                    Entry("src/Generated.g.cs", false, "after-generated"),
                    Entry("assets/Blob.bin", true, "after-binary"),
                    Entry("src/NoOp.cs", false, "same"),
                ]),
                []);

            var result = await new PostToolChangedPathResolver(
                new FakeProvenanceReader(["src/Shell.cs"])).ResolveAsync(request, CancellationToken.None);

            Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
            Assert.Equal(
                [
                    ("assets/Blob.bin", ChangedPathCategory.Binary),
                    ("src/Code.cs", ChangedPathCategory.Code),
                    ("src/Generated.g.cs", ChangedPathCategory.Generated),
                    ("src/NoOp.cs", ChangedPathCategory.NoOp),
                    ("src/Shell.cs", ChangedPathCategory.Code),
                ],
                result.Value.Paths.Select(static path => (path.RepositoryRelativePath, path.Category)));
            Assert.Equal(["src/Code.cs", "src/Shell.cs"], result.Value.CodePaths);
            Assert.Contains(ChangedPathEvidence.GitDelta, result.Value.Paths.Single(static path => path.RepositoryRelativePath == "src/Shell.cs").Evidence);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(state, recursive: true);
        }
    }

    private static WorkspacePathSnapshotEntry Entry(string path, bool isBinary, string hash) =>
        new(path, Exists: true, IsBinary: isBinary, ContentHash: hash);

    private sealed class FakeProvenanceReader(IReadOnlyList<string> changedPaths) : IRepositoryProvenanceReader
    {
        public ValueTask<Result<RepositoryProvenance>> ReadAsync(LocatedWorkspace workspace, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ResultFactory.Success(new RepositoryProvenance(
                "0123456789abcdef0123456789abcdef01234567",
                RepositoryWorktreeState.Dirty,
                changedPaths)));
    }
}
