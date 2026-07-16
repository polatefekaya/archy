using System.Text.Json;
using Archy.Features.Health.ReadHealthComponentPages;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Web.GraphApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Archy.Features.Web.HealthApi;

public static class HealthApiEndpoints
{
    public static void Map(WebApplication application,IServiceProvider? services,McpWorkspaceContext? workspace)=>application.MapGet("/api/v1/health/{snapshotId}",(string snapshotId,HttpRequest request,CancellationToken cancellationToken)=>GetAsync(snapshotId,request,services,workspace,cancellationToken));
    private static async Task<GraphApiResult> GetAsync(string id,HttpRequest request,IServiceProvider? services,McpWorkspaceContext? workspace,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(id)||id.Length>512||!Page(request,out var offset,out var limit))return GraphApiJsonWriter.Problem(400,"validation","A health snapshot ID, offset, and limit are required.");
        if(workspace is null||services?.GetService<IHealthComponentPageReader>() is not { } reader)return GraphApiJsonWriter.Problem(503,"workspace_unavailable","The local web host does not have health-read services for an initialized Archy workspace.");
        var result=await reader.ReadAsync(workspace.StateLocation,id,offset,limit,ct);if(!result.IsSuccess)return GraphApiJsonWriter.Problem(result.Problem!);using var stream=new MemoryStream();using(var w=new Utf8JsonWriter(stream)){w.WriteStartObject();w.WriteString("schema","archy.health-component-page/v1");w.WriteString("snapshotId",id);w.WriteNumber("graphRevision",result.Value!.GraphRevision);w.WriteString("calculationVersion",result.Value.CalculationVersion);w.WriteNumber("score",result.Value.Score);w.WriteNumber("offset",offset);w.WriteNumber("limit",limit);w.WriteNumber("totalCount",result.Value.TotalCount);w.WriteStartArray("items");foreach(var item in result.Value.Items){w.WriteStartObject();w.WriteString("key",item.ComponentKey);w.WriteNumber("rawValue",item.RawValue);w.WriteNumber("weight",item.Weight);w.WriteNumber("weightedContribution",item.WeightedContribution);w.WriteString("detail",item.DetailJson);w.WriteEndObject();}w.WriteEndArray();w.WriteEndObject();}return new GraphApiResult(stream.ToArray(),200);
    }
    private static bool Page(HttpRequest r,out int offset,out int limit){offset=0;limit=20;return(!r.Query.TryGetValue("offset",out var o)||o.Count==1&&int.TryParse(o[0],out offset)&&offset is>=0 and<=1_000_000)&&(!r.Query.TryGetValue("limit",out var l)||l.Count==1&&int.TryParse(l[0],out limit)&&limit is>=1 and<=100);}
}
