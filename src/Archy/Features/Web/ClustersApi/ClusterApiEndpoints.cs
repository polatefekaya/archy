using System.Text.Json;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Placement.ReadClusterMemberPages;
using Archy.Features.Web.GraphApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Archy.Features.Web.ClustersApi;

public static class ClusterApiEndpoints
{
    public static void Map(WebApplication application,IServiceProvider? services,McpWorkspaceContext? workspace)=>application.MapGet("/api/v1/clusters/{revisionId}/{clusterId}",(string revisionId,string clusterId,HttpRequest request,CancellationToken cancellationToken)=>GetAsync(revisionId,clusterId,request,services,workspace,cancellationToken));
    private static async Task<GraphApiResult> GetAsync(string revision,string cluster,HttpRequest request,IServiceProvider? services,McpWorkspaceContext? workspace,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(revision)||string.IsNullOrWhiteSpace(cluster)||revision.Length>512||cluster.Length>512||!Page(request,out var offset,out var limit))return GraphApiJsonWriter.Problem(400,"validation","Cluster revision, cluster ID, offset, and limit are required.");
        if(workspace is null||services?.GetService<IClusterMemberPageReader>() is not { } reader)return GraphApiJsonWriter.Problem(503,"workspace_unavailable","The local web host does not have cluster-read services for an initialized Archy workspace.");
        var result=await reader.ReadAsync(workspace.StateLocation,revision,cluster,offset,limit,ct);if(!result.IsSuccess)return GraphApiJsonWriter.Problem(result.Problem!);using var stream=new MemoryStream();using(var w=new Utf8JsonWriter(stream)){w.WriteStartObject();w.WriteString("schema","archy.cluster-member-page/v1");w.WriteString("clusterRevisionId",revision);w.WriteString("clusterId",cluster);w.WriteString("clusterKey",result.Value!.ClusterKey);w.WriteNumber("graphRevision",result.Value.GraphRevision);w.WriteNumber("offset",offset);w.WriteNumber("limit",limit);w.WriteNumber("totalCount",result.Value.TotalCount);w.WriteStartArray("items");foreach(var item in result.Value.Items){w.WriteStartObject();w.WriteString("targetKind",Archy.SharedKernel.Primitives.ArchitectureTargetCodec.ToStorageValue(item.Target.Kind));w.WriteString("targetStableId",item.Target.StableId);w.WriteNumber("membershipWeight",item.MembershipWeight);w.WriteEndObject();}w.WriteEndArray();w.WriteEndObject();}return new GraphApiResult(stream.ToArray(),200);
    }
    private static bool Page(HttpRequest r,out int offset,out int limit){offset=0;limit=50;return(!r.Query.TryGetValue("offset",out var o)||o.Count==1&&int.TryParse(o[0],out offset)&&offset is>=0 and<=1_000_000)&&(!r.Query.TryGetValue("limit",out var l)||l.Count==1&&int.TryParse(l[0],out limit)&&limit is>=1 and<=200);}
}
