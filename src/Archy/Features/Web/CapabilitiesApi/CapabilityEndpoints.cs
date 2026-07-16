using System.Text.Json;
using Archy.Features.Integrations.Codex.RecordHookEvents;
using Archy.Features.Memory.ModelProviders.OpenAi;
using Archy.Features.Web.GraphApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Archy.Features.Web.CapabilitiesApi;

/// <summary>Reports local feature readiness without exposing credentials or implementation secrets.</summary>
public static class CapabilityEndpoints
{
    public static void Map(WebApplication application, IServiceProvider? services) => application.MapGet("/api/v1/capabilities", () => Write(services));
    private static GraphApiResult Write(IServiceProvider? services)
    {
        var modelReady = !string.IsNullOrWhiteSpace(services?.GetService<IOpenAiApiKeyProvider>()?.GetApiKey());
        var eventReady = services?.GetService<IHookEventPublisher>() is not null;
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteString("schema", "archy.capabilities/v1"); writer.WriteStartArray("capabilities");
            WriteCapability(writer, "model", modelReady, modelReady ? null : "OPENAI_API_KEY is not configured; model-backed summaries remain unavailable.");
            WriteCapability(writer, "liveEvents", eventReady, eventReady ? null : "Live event publication is unavailable in this host.");
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        return new GraphApiResult(stream.ToArray(), StatusCodes.Status200OK);
    }
    private static void WriteCapability(Utf8JsonWriter writer, string name, bool ready, string? warning) { writer.WriteStartObject(); writer.WriteString("name", name); writer.WriteBoolean("ready", ready); if (warning is null) writer.WriteNull("warning"); else writer.WriteString("warning", warning); writer.WriteEndObject(); }
}
