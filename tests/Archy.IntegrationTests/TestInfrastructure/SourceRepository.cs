namespace Archy.IntegrationTests.TestInfrastructure;

internal static class SourceRepository
{
    public static string Root { get; } = LocateRoot();

    private static string LocateRoot()
    {
        for (var candidate = new DirectoryInfo(AppContext.BaseDirectory); candidate is not null; candidate = candidate.Parent)
        {
            if (File.Exists(Path.Combine(candidate.FullName, "Archy.sln")))
            {
                return candidate.FullName;
            }
        }

        throw new DirectoryNotFoundException("The integration test could not locate the Archy source repository root.");
    }
}
