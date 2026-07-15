namespace Archy.Features.Duplicates.CompareInterfaceSignatures;

/// <summary>Produces independently explainable API-shape evidence; it does not decide whether two implementations are duplicates.</summary>
public sealed class InterfaceSignatureSimilarityScorer : IInterfaceSignatureSimilarityScorer
{
    public InterfaceSignatureSimilarity Score(InterfaceSignature left, InterfaceSignature right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        Validate(left);
        Validate(right);

        var parameterScore = SequenceScore(left.ParameterTypes, right.ParameterTypes);
        var returnScore = TypeScore(left.ReturnType, right.ReturnType);
        var genericScore = left.GenericArity == right.GenericArity ? 1d : 0d;
        var nameScore = NameScore(left.Name, right.Name);
        var overall = (parameterScore * .35d) + (returnScore * .20d) + (genericScore * .15d) + (nameScore * .30d);
        return new InterfaceSignatureSimilarity(
            left.SymbolId,
            right.SymbolId,
            Math.Round(overall, 6, MidpointRounding.AwayFromZero),
            parameterScore,
            returnScore,
            genericScore,
            nameScore);
    }

    private static void Validate(InterfaceSignature value)
    {
        if (string.IsNullOrWhiteSpace(value.SymbolId) || string.IsNullOrWhiteSpace(value.Name) || value.ParameterTypes is null ||
            value.ParameterTypes.Any(static type => string.IsNullOrWhiteSpace(type)) || value.GenericArity is < 0 or > 32)
        {
            throw new ArgumentException("Interface signatures require a symbol, name, complete parameter types, and a supported generic arity.", nameof(value));
        }
    }

    private static double SequenceScore(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count == 0 && right.Count == 0)
        {
            return 1d;
        }

        var length = Math.Max(left.Count, right.Count);
        var total = 0d;
        for (var index = 0; index < length; index++)
        {
            total += index < left.Count && index < right.Count ? TypeScore(left[index], right[index]) : 0d;
        }

        return Math.Round(total / length, 6, MidpointRounding.AwayFromZero);
    }

    private static double TypeScore(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
        {
            return 1d;
        }

        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return 0d;
        }

        return string.Equals(NormalizeType(left), NormalizeType(right), StringComparison.Ordinal) ? 1d : 0d;
    }

    private static double NameScore(string left, string right)
    {
        var leftTokens = Tokenize(left);
        var rightTokens = Tokenize(right);
        var union = leftTokens.Union(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        var jaccard = union == 0 ? 1d : leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count() / (double)union;
        var normalizedEdit = 1d - (Levenshtein(left.ToUpperInvariant(), right.ToUpperInvariant()) / (double)Math.Max(left.Length, right.Length));
        return Math.Round((jaccard * .6d) + (normalizedEdit * .4d), 6, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeType(string value) => string.Concat(value.Where(static character => !char.IsWhiteSpace(character))).Replace("global::", string.Empty, StringComparison.Ordinal);

    private static string[] Tokenize(string name)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        for (var index = 0; index < name.Length; index++)
        {
            var currentCharacter = name[index];
            var nextStartsToken = index > 0 && char.IsUpper(currentCharacter) && (char.IsLower(name[index - 1]) || char.IsDigit(name[index - 1]));
            if (!char.IsLetterOrDigit(currentCharacter) || nextStartsToken)
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                if (!char.IsLetterOrDigit(currentCharacter)) continue;
            }

            current.Append(currentCharacter);
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
}
