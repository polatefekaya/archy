using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Archy.Features.Health.HealthSnapshots;
using Archy.Features.Health.ReadHealthComponentPages;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Web.RunLocalWebHost;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Archy.IntegrationTests.Features.Web.HealthApi;

public sealed class HealthApiEndpointsTests
{
    [Fact]
    public async Task ServesPagedHealthComponentsOverTheRealLoopbackRoute()
    {
        using var fixture=WorkspaceStateFixture.Create();var init=await fixture.InitializeAsync();Assert.True(init.IsSuccess);var revision=await GraphRevisionTestBuilder.CommitAsync(init.Value.StateLocation,[GraphRevisionTestBuilder.Node("node:h","h")]);var snapshot=await new HealthSnapshotRepository(TimeProvider.System,new WorkspaceLockManager(TimeProvider.System)).RecordAsync(init.Value.StateLocation,new HealthSnapshotFact(revision,"v",80,[new HealthMetricComponentFact("component",1,1,1,"{}")],[]),CancellationToken.None);Assert.True(snapshot.IsSuccess);
        var services=new ServiceCollection();services.AddSingleton<IWorkspaceLockManager>(_=>new WorkspaceLockManager(TimeProvider.System));services.AddSingleton<IHealthComponentPageReader,HealthComponentPageReader>();using var provider=services.BuildServiceProvider();var port=Port();Assert.True(LocalWebHostOptions.TryCreate(port,out var options));using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(15));var run=ArchyLocalWebHost.RunAsync(options!,provider,new McpWorkspaceContext(fixture.Repository.Root,init.Value.StateLocation),cancel.Token);using var client=new HttpClient{BaseAddress=new Uri($"http://127.0.0.1:{port}")};_=await Ready(client,cancel.Token);
        var response=await client.GetAsync($"/api/v1/health/{snapshot.Value.HealthSnapshotId}?limit=1",cancel.Token);using var json=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancel.Token));Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("archy.health-component-page/v1",json.RootElement.GetProperty("schema").GetString());Assert.Equal(80,json.RootElement.GetProperty("score").GetDouble());await cancel.CancelAsync();Assert.Equal(0,await run);
    }
    private static int Port(){using var l=new TcpListener(IPAddress.Loopback,0);l.Start();return ((IPEndPoint)l.LocalEndpoint).Port;} private static async Task<string> Ready(HttpClient c,CancellationToken t){for(var i=0;i<30;i++){try{return await c.GetStringAsync("/health",t);}catch(HttpRequestException)when(i<29){await Task.Delay(100,t);}}throw new InvalidOperationException();}
}
