using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Decisions.ReadDecisionPages;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Web.RunLocalWebHost;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;
using Archy.SharedKernel.Primitives;
using Microsoft.Extensions.DependencyInjection;

namespace Archy.IntegrationTests.Features.Web.DecisionsApi;

public sealed class DecisionApiEndpointsTests
{
    [Fact]
    public async Task ServesTargetScopedDecisionPageAndRejectsUnknownTargetKinds()
    {
        using var fixture=WorkspaceStateFixture.Create();var initialized=await fixture.InitializeAsync();Assert.True(initialized.IsSuccess);
        var node=GraphRevisionTestBuilder.Node("node:decision","hash");var revision=await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation,[node]);
        var recorded=await new ArchitectureDecisionRepository(TimeProvider.System,new WorkspaceLockManager(TimeProvider.System)).RecordAsync(initialized.Value.StateLocation,new ArchitectureDecisionFact("review",DecisionResolution.Accepted,"ok","agent","fixture",null,revision,[new ArchitectureTarget(ArchitectureTargetKind.GraphNode,node.StableId)]),CancellationToken.None);Assert.True(recorded.IsSuccess);
        using var provider=Services();var port=Port();Assert.True(LocalWebHostOptions.TryCreate(port,out var options));using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(15));var run=ArchyLocalWebHost.RunAsync(options!,provider,new McpWorkspaceContext(fixture.Repository.Root,initialized.Value.StateLocation),cancellation.Token);using var client=new HttpClient{BaseAddress=new Uri($"http://127.0.0.1:{port}")};_=await Ready(client,cancellation.Token);
        var response=await client.GetAsync($"/api/v1/decisions/graph_node/{node.StableId}?limit=1",cancellation.Token);var invalid=await client.GetAsync($"/api/v1/decisions/nope/{node.StableId}",cancellation.Token);using var body=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellation.Token));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("archy.decision-page/v1",body.RootElement.GetProperty("schema").GetString());Assert.Equal(recorded.Value.DecisionId,body.RootElement.GetProperty("items")[0].GetProperty("decisionId").GetString());Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        await cancellation.CancelAsync();Assert.Equal(0,await run);
    }
    private static ServiceProvider Services(){var s=new ServiceCollection();s.AddSingleton<IWorkspaceLockManager>(_=>new WorkspaceLockManager(TimeProvider.System));s.AddSingleton<IArchitectureDecisionPageReader,ArchitectureDecisionPageReader>();return s.BuildServiceProvider();}
    private static int Port(){using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();return ((IPEndPoint)listener.LocalEndpoint).Port;}
    private static async Task<string> Ready(HttpClient c,CancellationToken ct){for(var i=0;i<30;i++){try{return await c.GetStringAsync("/health",ct);}catch(HttpRequestException)when(i<29){await Task.Delay(100,ct);}}throw new InvalidOperationException();}
}
