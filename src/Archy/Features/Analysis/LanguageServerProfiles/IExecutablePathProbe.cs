namespace Archy.Features.Analysis.LanguageServerProfiles;

public interface IExecutablePathProbe
{
    string? Resolve(string command, string workingDirectory);
}
