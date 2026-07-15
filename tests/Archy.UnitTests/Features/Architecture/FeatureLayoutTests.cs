namespace Archy.UnitTests.Features.Architecture;

public sealed class FeatureLayoutTests
{
    [Fact]
    public void FeatureFoldersDoNotUseCatchAllOperationNames()
    {
        var featureRoot = LocateFeatureRoot();
        var forbiddenRelativePaths = new[]
        {
            Path.Combine("Sessions", "ManageArchitectureSession"),
            Path.Combine("Storage", "MaintainWorkspaceDatabase"),
            Path.Combine("Storage", "InitializeWorkspaceDatabase"),
            Path.Combine("Decisions", "RecordArchitectureDecision"),
            Path.Combine("Duplicates", "RecordDuplicateFinding"),
            Path.Combine("Memory", "RecordSummaryVersion"),
            Path.Combine("Placement", "RecordClusterRevision"),
            Path.Combine("Health", "RecordHealthSnapshot"),
            Path.Combine("Storage", "AnalysisRuns"),
        };

        var obsoleteDirectories = forbiddenRelativePaths
            .Select(path => Path.Combine(featureRoot, path))
            .Where(Directory.Exists)
            .ToArray();

        Assert.Empty(obsoleteDirectories);
    }

    [Fact]
    public void FeatureSourceDoesNotContainGenericStoreOrMisplacedCliAdapters()
    {
        var featureRoot = LocateFeatureRoot();
        var commandLineRoot = Path.Combine(featureRoot, "CommandLine");
        var sourceFiles = Directory.EnumerateFiles(featureRoot, "*.cs", SearchOption.AllDirectories).ToArray();

        var genericStores = sourceFiles
            .Select(static path => Path.GetFileNameWithoutExtension(path) ?? string.Empty)
            .Where(static name => name.EndsWith("Store", StringComparison.Ordinal))
            .ToArray();
        var misplacedCliAdapters = sourceFiles
            .Where(static path => (Path.GetFileNameWithoutExtension(path) ?? string.Empty).EndsWith("Cli", StringComparison.Ordinal))
            .Where(path => !path.StartsWith(commandLineRoot, StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(genericStores);
        Assert.Empty(misplacedCliAdapters);
    }

    [Fact]
    public void WorkspaceDatabaseUsesDedicatedOperationalSlices()
    {
        var databaseRoot = Path.Combine(LocateFeatureRoot(), "Storage", "WorkspaceDatabase");
        var requiredSlices = new[]
        {
            "Initialize",
            "Integrity",
            "Check",
            "Backup",
            "Restore",
            "Vacuum",
            "ExportDiagnostics",
        };
        var actualSlices = Directory.EnumerateDirectories(databaseRoot)
            .Select(static path => Path.GetFileName(path) ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(requiredSlices, slice => Assert.Contains(slice, actualSlices));
    }

    private static string LocateFeatureRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var featureRoot = Path.Combine(directory.FullName, "src", "Archy", "Features");
            if (Directory.Exists(featureRoot))
            {
                return featureRoot;
            }
        }

        throw new DirectoryNotFoundException("Could not locate src/Archy/Features from the test runtime directory.");
    }
}
