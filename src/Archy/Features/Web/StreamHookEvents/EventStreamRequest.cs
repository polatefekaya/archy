using Microsoft.AspNetCore.Http;

namespace Archy.Features.Web.StreamHookEvents;

/// <summary>Validated reconnect cursor for the per-session local event stream.</summary>
public sealed record EventStreamRequest(string SessionId, int AfterSequence)
{
    public static bool TryParse(HttpRequest request, out EventStreamRequest? value, out string? error)
    {
        value = null;
        error = null;
        if (!request.Query.TryGetValue("sessionId", out var sessionValues)
            || sessionValues.Count != 1
            || string.IsNullOrWhiteSpace(sessionValues[0])
            || sessionValues[0]!.Length > 128)
        {
            error = "Query parameter 'sessionId' must contain one non-empty value of at most 128 characters.";
            return false;
        }

        var after = 0;
        if (request.Query.TryGetValue("after", out var afterValues)
            && (afterValues.Count != 1
                || !int.TryParse(afterValues[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out after)
                || after < 0))
        {
            error = "Query parameter 'after' must be one non-negative sequence number.";
            return false;
        }

        value = new EventStreamRequest(sessionValues[0]!, after);
        return true;
    }
}
