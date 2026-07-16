using System.Text.Json;
using Archy.Features.Duplicates.ReadDuplicateObservationPages;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Web.GraphApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Archy.Features.Web.DuplicatesApi;

public static class DuplicateApiEndpoints
{
    public static void Map(WebApplication application, IServiceProvider? services, McpWorkspaceContext? workspace) => application.MapGet("/api/v1/duplicates/{findingId}", (string findingId, HttpRequest request, CancellationToken cancellationToken) => GetAsync(findingId, request, services, workspace, cancellationToken));
    private static async Task<GraphApiResult> GetAsync(string id,HttpRequest request,IServiceProvider? services,McpWorkspaceContext? workspace,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(id)||id.Length>512||!Page(request,out var offset,out var limit))return GraphApiJsonWriter.Problem(400,"validation","A finding ID, offset, and limit are required.");
        if(workspace is null||services?.GetService<IDuplicateObservationPageReader>() is not { } reader)return GraphApiJsonWriter.Problem(503,"workspace_unavailable","The local web host does not have duplicate-read services for an initialized Archy workspace.");
        var result=await reader.ReadAsync(workspace.StateLocation,id,offset,limit,ct); if(!result.IsSuccess)return GraphApiJsonWriter.Problem(result.Problem!);
        using var stream=new MemoryStream();using(var w=new Utf8JsonWriter(stream)){w.WriteStartObject();w.WriteString("schema","archy.duplicate-observation-page/v1");w.WriteString("findingId",result.Value!.FindingId);w.WriteNumber("offset",offset);w.WriteNumber("limit",limit);w.WriteNumber("totalCount",result.Value.TotalCount);w.WriteStartArray("items");foreach(var item in result.Value.Items){w.WriteStartObject();w.WriteString("observationId",item.ObservationId);w.WriteNumber("graphRevision",item.GraphRevision);w.WriteString("aggregationVersion",item.AggregationVersion);w.WriteNumber("confidence",item.Confidence);w.WriteString("rationale",item.RationaleJson);w.WriteString("observedAtUtc",item.ObservedAtUtc);w.WriteEndObject();}w.WriteEndArray();w.WriteEndObject();}return new GraphApiResult(stream.ToArray(),200);
    }
    private static bool Page(HttpRequest r,out int offset,out int limit){offset=0;limit=20;return(!r.Query.TryGetValue("offset",out var o)||o.Count==1&&int.TryParse(o[0],out offset)&&offset is>=0 and<=1_000_000)&&(!r.Query.TryGetValue("limit",out var l)||l.Count==1&&int.TryParse(l[0],out limit)&&limit is>=1 and<=100);}
}
