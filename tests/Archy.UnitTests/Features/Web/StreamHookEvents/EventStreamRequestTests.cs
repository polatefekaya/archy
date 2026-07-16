using Archy.Features.Web.StreamHookEvents;
using Microsoft.AspNetCore.Http;

namespace Archy.UnitTests.Features.Web.StreamHookEvents;

public sealed class EventStreamRequestTests
{
    [Fact]
    public void ParsesOneSessionAndReconnectCursor()
    {
        var parsed = EventStreamRequest.TryParse(Request("?sessionId=session-1&after=17"), out var request, out var error);

        Assert.True(parsed, error);
        Assert.Equal("session-1", request!.SessionId);
        Assert.Equal(17, request.AfterSequence);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?sessionId=")]
    [InlineData("?sessionId=a&sessionId=b")]
    [InlineData("?sessionId=session&after=-1")]
    [InlineData("?sessionId=session&after=1.5")]
    [InlineData("?sessionId=session&after=1&after=2")]
    public void RejectsAmbiguousOrInvalidReconnectCursors(string query)
    {
        var parsed = EventStreamRequest.TryParse(Request(query), out _, out var error);

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
