using System.Text.RegularExpressions;

namespace Archy.Features.CommandLine.Embeddings;

/// <summary>Performs a minimal, comment-preserving update of the two TOML sections that opt a repository into embeddings.</summary>
public static partial class ConfigureEmbeddingSetup
{
    public static string Apply(string existing, string provider, string model)
    {
        if (!Identifier().IsMatch(provider) || !Identifier().IsMatch(model)) throw new ArgumentException("Provider and model must contain only letters, digits, '.', '_' or '-'.");
        var document = existing.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!SchemaVersion().IsMatch(document)) document = "schema_version = 1\n\n" + document.TrimStart();
        document = Set(document, "model", "provider", Quote(provider));
        document = Set(document, "model", "embedding_model", Quote(model));
        return Set(document, "memory", "source_sharing", Quote("summaries_and_embeddings"));
    }

    private static string Set(string document, string section, string key, string value)
    {
        var lines = document.Split('\n').ToList();
        var sectionHeader = $"[{section}]";
        var start = lines.FindIndex(line => string.Equals(line.Trim(), sectionHeader, StringComparison.Ordinal));
        if (start < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1])) lines.Add(string.Empty);
            lines.Add(sectionHeader);
            lines.Add($"{key} = {value}");
            return string.Join('\n', lines).TrimEnd() + Environment.NewLine;
        }

        var end = lines.FindIndex(start + 1, line => line.TrimStart().StartsWith('['));
        if (end < 0) end = lines.Count;
        var keyExpression = new Regex($@"^\s*{Regex.Escape(key)}\s*=", RegexOptions.CultureInvariant);
        for (var index = start + 1; index < end; index++)
        {
            if (!keyExpression.IsMatch(lines[index])) continue;
            lines[index] = $"{key} = {value}";
            return string.Join('\n', lines).TrimEnd() + Environment.NewLine;
        }
        lines.Insert(end, $"{key} = {value}");
        return string.Join('\n', lines).TrimEnd() + Environment.NewLine;
    }

    private static string Quote(string value) => $"\"{value}\"";
    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
    [GeneratedRegex("(?m)^\\s*schema_version\\s*=", RegexOptions.CultureInvariant)]
    private static partial Regex SchemaVersion();
}
