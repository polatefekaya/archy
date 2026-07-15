namespace Archy.Features.Placement.MineSiblingNaming;

public interface ISiblingNamingConventionMiner
{
    NamingSuggestion? Suggest(string proposedStem, IReadOnlyList<SiblingNamingSample> siblings);
}
