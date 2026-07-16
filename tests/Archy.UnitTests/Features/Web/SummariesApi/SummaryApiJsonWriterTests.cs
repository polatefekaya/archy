using System.Text.Json;
using Archy.Features.Memory.ReadSummaryPages;
using Archy.Features.Memory.Summaries;
using Archy.Features.Web.SummariesApi;

namespace Archy.UnitTests.Features.Web.SummariesApi;

public sealed class SummaryApiJsonWriterTests
{
    [Fact]
    public async Task BoundsLargeSummaryTextWhileRetainingVersionIdentity()
    {
        var item = new SummaryVersion("version-1", "summary-1", 1, 4, null, new string('x', 40_000), "diff", "fixture", "none", "{}", SummaryStaleness.Fresh, null, DateTimeOffset.UnixEpoch);

        var result = SummaryApiJsonWriter.Page(new SummaryVersionPage("summary-1", 0, 1, 1, [item]));
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var document = JsonDocument.Parse(context.Response.Body);
        var serialized = document.RootElement.GetProperty("items")[0];

        Assert.Equal("version-1", serialized.GetProperty("summaryVersionId").GetString());
        Assert.True(serialized.GetProperty("summaryTextTruncated").GetBoolean());
        Assert.Equal(32_768, serialized.GetProperty("summaryText").GetString()!.Length);
    }
}
