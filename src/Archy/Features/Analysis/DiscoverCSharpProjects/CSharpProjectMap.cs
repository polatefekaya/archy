namespace Archy.Features.Analysis.DiscoverCSharpProjects;

public sealed record CSharpProjectMap(
    string RepositoryRoot,
    IReadOnlyList<string> SolutionPaths,
    IReadOnlyList<CSharpProject> Projects,
    IReadOnlyList<CSharpProjectDiscoveryDiagnostic> Diagnostics);

public sealed record CSharpProject(
    string RepositoryRelativePath,
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> SourceRoots,
    CSharpGeneratedCodeSettings GeneratedCode);

public sealed record CSharpGeneratedCodeSettings(
    bool EnableDefaultCompileItems,
    bool EmitCompilerGeneratedFiles,
    string? CompilerGeneratedFilesOutputPath);

public sealed record CSharpProjectDiscoveryDiagnostic(string RepositoryRelativePath, string Code, string Message);
