namespace Archy.Features.Placement.MineSiblingNaming;

/// <summary>Infers only unanimous file/type suffix conventions from enough sibling samples; mixed conventions explicitly abstain.</summary>
public sealed class SiblingNamingConventionMiner : ISiblingNamingConventionMiner
{
    public NamingSuggestion? Suggest(string proposedStem, IReadOnlyList<SiblingNamingSample> siblings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposedStem);
        ArgumentNullException.ThrowIfNull(siblings);
        if (siblings.Any(static sibling => sibling is null || string.IsNullOrWhiteSpace(sibling.RepositoryRelativePath) || string.IsNullOrWhiteSpace(sibling.TypeName)))
        {
            throw new ArgumentException("Sibling naming samples require a path and type name.", nameof(siblings));
        }

        var suffixes = siblings
            .Select(static sibling => Suffix(sibling.TypeName))
            .Where(static suffix => suffix is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (siblings.Count < 3 || suffixes.Length != 1 || siblings.Any(static sibling => !string.Equals(Path.GetFileNameWithoutExtension(sibling.RepositoryRelativePath), sibling.TypeName, StringComparison.Ordinal)))
        {
            return null;
        }

        var typeName = string.Concat(proposedStem.Trim(), suffixes[0]);
        return new NamingSuggestion(typeName, string.Concat(typeName, ".cs"), $"{siblings.Count} sibling types consistently use the '{suffixes[0]}' suffix and matching .cs file names.");
    }

    private static string? Suffix(string typeName)
    {
        var index = Enumerable.Range(1, typeName.Length - 1).FirstOrDefault(index => char.IsUpper(typeName[index]) && char.IsLower(typeName[index - 1]));
        return index == 0 || index == typeName.Length - 1 ? null : typeName[index..];
    }
}
