namespace Archy.Features.Analysis.LanguageServerProfiles;

public sealed class PathExecutablePathProbe : IExecutablePathProbe
{
    public string? Resolve(string command, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        if (Path.IsPathFullyQualified(command) || command.Contains(Path.DirectorySeparatorChar) || command.Contains(Path.AltDirectorySeparatorChar))
        {
            var candidate = Path.IsPathFullyQualified(command) ? command : Path.Combine(workingDirectory, command);
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, command);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }
}
