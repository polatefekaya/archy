namespace Archy.Features.Duplicates.CheckDataShapeNaming;

/// <summary>Produces a narrowly-scoped convention advisory for highly similar records/DTOs instead of a false duplicate-logic alert.</summary>
public sealed class DataShapeNamingChecker : IDataShapeNamingChecker
{
    private const double SemanticThreshold = .80d;
    private const double NameThreshold = .45d;

    public DataShapeNamingAdvisory? Check(DataShapeNamingComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        Validate(comparison);
        var (left, right) = Order(comparison.Left, comparison.Right);
        var nameSimilarity = NameSimilarity(left.DisplayName, right.DisplayName);
        if (comparison.SemanticSimilarity < SemanticThreshold || nameSimilarity < NameThreshold)
        {
            return null;
        }

        return new DataShapeNamingAdvisory(
            left.StableId,
            right.StableId,
            Round(comparison.SemanticSimilarity),
            Round(nameSimilarity),
            $"'{left.DisplayName}' and '{right.DisplayName}' are highly similar {Describe(left.Kind)} shapes; review whether their names clearly communicate distinct roles.");
    }

    private static void Validate(DataShapeNamingComparison comparison)
    {
        if (comparison.Left is null || comparison.Right is null || string.IsNullOrWhiteSpace(comparison.Left.StableId) || string.IsNullOrWhiteSpace(comparison.Right.StableId) ||
            string.IsNullOrWhiteSpace(comparison.Left.DisplayName) || string.IsNullOrWhiteSpace(comparison.Right.DisplayName) || !Enum.IsDefined(comparison.Left.Kind) || !Enum.IsDefined(comparison.Right.Kind) ||
            string.Equals(comparison.Left.StableId, comparison.Right.StableId, StringComparison.Ordinal) || !double.IsFinite(comparison.SemanticSimilarity) || comparison.SemanticSimilarity is < -1d or > 1d)
        {
            throw new ArgumentException("Data-shape naming checks require two distinct named records or DTOs and a finite similarity score.", nameof(comparison));
        }
    }

    private static (DataShapeNamingCandidate Left, DataShapeNamingCandidate Right) Order(DataShapeNamingCandidate first, DataShapeNamingCandidate second) =>
        string.CompareOrdinal(first.StableId, second.StableId) <= 0 ? (first, second) : (second, first);

    private static string Describe(DataShapeKind kind) => kind == DataShapeKind.Record ? "record" : "DTO";

    private static double NameSimilarity(string left, string right)
    {
        var leftTokens = Tokens(left);
        var rightTokens = Tokens(right);
        var union = leftTokens.Union(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        if (union == 0) return 0d;
        var jaccard = leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count() / (double)union;
        var maxLength = Math.Max(left.Length, right.Length);
        var edit = maxLength == 0 ? 0d : 1d - Levenshtein(left.ToUpperInvariant(), right.ToUpperInvariant()) / (double)maxLength;
        return (jaccard * .6d) + (edit * .4d);
    }

    private static string[] Tokens(string value)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var startsToken = index > 0 && char.IsUpper(character) && (char.IsLower(value[index - 1]) || char.IsDigit(value[index - 1]));
            if (!char.IsLetterOrDigit(character) || startsToken)
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                if (!char.IsLetterOrDigit(character)) continue;
            }

            current.Append(character);
        }

        if (current.Length > 0) tokens.Add(current.ToString());
        return [.. tokens];
    }

    private static int Levenshtein(string left, string right)
    {
        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), previous[column - 1] + (left[row - 1] == right[column - 1] ? 0 : 1));
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static double Round(double value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);
}
