using Microsoft.AspNetCore.Http;

namespace Archy.Features.Web.GraphApi;

/// <summary>Small raw JSON result so response status and AOT-safe bytes are controlled together.</summary>
public sealed class GraphApiResult(byte[] payload, int statusCode) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        httpContext.Response.ContentLength = payload.Length;
        await httpContext.Response.Body.WriteAsync(payload, httpContext.RequestAborted);
    }
}
