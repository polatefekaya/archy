using System.Text.Json;
using Archy.Features.Memory.ReadSummaryPages;
using Archy.Features.Web.GraphApi;
using Microsoft.AspNetCore.Http;

namespace Archy.Features.Web.SummariesApi;

/// <summary>AOT-safe public representation of bounded summary history.</summary>
public static class SummaryApiJsonWriter
{
    private const int MaximumTextCharacters = 32_768;

    public static GraphApiResult Page(SummaryVersionPage page)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "archy.summary-page/v1");
            writer.WriteString("summaryId", page.SummaryId);
            writer.WriteNumber("offset", page.Offset);
            writer.WriteNumber("limit", page.Limit);
            writer.WriteNumber("totalCount", page.TotalCount);
            writer.WriteStartArray("items");
            foreach (var item in page.Items)
            {
                writer.WriteStartObject();
                writer.WriteString("summaryVersionId", item.SummaryVersionId);
                writer.WriteNumber("version", item.VersionNumber);
                writer.WriteNumber("sourceGraphRevision", item.SourceGraphRevision);
                writer.WriteString("staleness", item.Staleness.ToString().ToLowerInvariant());
                writer.WriteString("summaryText", Bound(item.SummaryText, out var summaryTruncated));
                writer.WriteBoolean("summaryTextTruncated", summaryTruncated);
                writer.WriteString("englishDiff", Bound(item.EnglishDiff, out var diffTruncated));
                writer.WriteBoolean("englishDiffTruncated", diffTruncated);
                writer.WriteString("provider", item.Provider);
                writer.WriteString("model", item.Model);
                writer.WriteString("createdAtUtc", item.CreatedAtUtc);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return new GraphApiResult(stream.ToArray(), StatusCodes.Status200OK);
    }

    private static string Bound(string value, out bool truncated)
    {
        truncated = value.Length > MaximumTextCharacters;
        return truncated ? value[..MaximumTextCharacters] : value;
    }
}
