using Archy.Features.Web.GraphApi;
using Microsoft.AspNetCore.Http;

namespace Archy.UnitTests.Features.Web.GraphApi;

public sealed class GraphApiRequestParserTests
{
    [Theory]
    [InlineData("?revision=42&offset=7&limit=23", 42L, 7, 23)]
    [InlineData("", null, 0, GraphApiRequestParser.DefaultPageLimit)]
    public void PageAcceptsOnlyBoundedCanonicalIntegers(string query, long? expectedRevision, int expectedOffset, int expectedLimit)
    {
        var request = Request(query);

        var parsed = GraphApiRequestParser.TryParsePage(request, out var revision, out var offset, out var limit, out var error);

        Assert.True(parsed, error);
        Assert.Equal(expectedRevision, revision);
        Assert.Equal(expectedOffset, offset);
        Assert.Equal(expectedLimit, limit);
    }

    [Theory]
    [InlineData("?revision=0")]
    [InlineData("?revision=1.0")]
    [InlineData("?revision=+1")]
    [InlineData("?limit=0")]
    [InlineData("?limit=501")]
    [InlineData("?offset=-1")]
    [InlineData("?offset=1000001")]
    [InlineData("?limit=1&limit=2")]
    public void PageRejectsAmbiguousMalformedAndUnboundedInputs(string query)
    {
        var parsed = GraphApiRequestParser.TryParsePage(Request(query), out _, out _, out _, out var error);

        Assert.False(parsed);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("?depth=1", 1)]
    [InlineData("?depth=6", 6)]
    [InlineData("", GraphApiRequestParser.DefaultTraversalDepth)]
    public void TraversalAcceptsBoundedDepth(string query, int expectedDepth)
    {
        var parsed = GraphApiRequestParser.TryParseTraversal(Request(query), out _, out var depth, out var error);

        Assert.True(parsed, error);
        Assert.Equal(expectedDepth, depth);
    }

    [Theory]
    [InlineData("?depth=0")]
    [InlineData("?depth=7")]
    [InlineData("?depth=01")]
    [InlineData("?depth=2&depth=3")]
    public void TraversalRejectsOutOfRangeAndNonCanonicalDepth(string query)
    {
        var parsed = GraphApiRequestParser.TryParseTraversal(Request(query), out _, out _, out var error);

        Assert.False(parsed);
        Assert.NotNull(error);
    }

    private static HttpRequest Request(string query)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        return context.Request;
    }
}
