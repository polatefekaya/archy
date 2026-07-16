using System.Text;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphPage;
using Archy.Features.Web.GraphApi;
using Microsoft.AspNetCore.Http;

namespace Archy.UnitTests.Features.Web.GraphApi;

public sealed class GraphApiJsonWriterTests
{
    [Fact]
    public async Task PageEscapesUntrustedEvidenceAndKeepsThePublicEnvelopeBounded()
    {
        var node = new GraphNodeFact(
            "node:unsafe",
            "type",
            "unsafe",
            "unsafe",
            null,
            null,
            null,
            "fixture",
            1,
            "</script><script>alert(1)</script>",
            "hash");
        var result = GraphApiJsonWriter.Page(new GraphRevisionPage(
            GraphRevisionFactKind.Nodes,
            9,
            0,
            1,
            1,
            [node],
            []));

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", context.Response.ContentType);
        Assert.Contains("\"schema\":\"archy.graph-page/v1\"", body, StringComparison.Ordinal);
        Assert.Contains("\\u003C/script\\u003E", body, StringComparison.Ordinal);
        Assert.DoesNotContain("</script>", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProblemControlsFailureStatusAndDoesNotExposeFrameworkHtml()
    {
        var result = GraphApiJsonWriter.Problem(StatusCodes.Status400BadRequest, "validation", "limit is invalid");
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("{\"schema\":\"archy.problem/v1\",\"code\":\"validation\",\"message\":\"limit is invalid\"}", body);
    }
}
