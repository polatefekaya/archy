using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Integrations.Codex.RecordHookEvents;
using Archy.Features.Duplicates.EmbeddingCache;
using Archy.Features.Duplicates.IndexEmbeddings;
using Archy.Features.Memory.AuthorizeAiSourceSharing;
using Microsoft.Extensions.DependencyInjection;
using Mediator;
namespace Archy.Features.CommandLine.Integrations;

public static class McpCli
{
    public static Task<int> RunAsync(string[] args, IMediator mediator, CancellationToken cancellationToken)
        => RunAsync(args, mediator, serviceProvider: null, cancellationToken);

    public static Task<int> RunAsync(string[] args, IMediator mediator, IServiceProvider? serviceProvider, CancellationToken cancellationToken)
    {
        var workspacePath = Environment.CurrentDirectory;
        if (args.Length == 2 && args[0] == "stdio")
        {
            workspacePath = args[1];
        }
        else if (IsHttpCommand(args))
        {
            if (!int.TryParse(args[2], out var parsedPort)
                || !McpHttpServerOptions.TryCreate(parsedPort, args[4], out var options, out _))
            {
                return Task.FromResult(64);
            }

            if (args.Length == 6)
            {
                workspacePath = args[5];
            }

            var httpRouter = new McpRequestRouter(CreateCatalog(mediator, serviceProvider));
            return new McpHttpServer(new McpWorkspaceContextFactory(mediator), httpRouter, options!).RunAsync(workspacePath, cancellationToken);
        }
        else if (args.Length > 0 && args is not ["stdio"])
        {
            return Task.FromResult(64);
        }

        var router = new McpRequestRouter(CreateCatalog(mediator, serviceProvider));
        return new McpStdioServer(new McpWorkspaceContextFactory(mediator), router).RunAsync(workspacePath, cancellationToken);
    }

    private static McpToolCatalog CreateCatalog(IMediator mediator, IServiceProvider? serviceProvider) => new([
        new ResolveSymbolMcpTool(),
        new GetDependentsMcpTool(),
        new GetDoctorReadinessMcpTool(mediator),
        new CheckViolationMcpTool(mediator),
        new GetModuleRulesMcpTool(mediator),
        new CheckDuplicateMcpTool(),
        new FindSimilarMcpTool(
            mediator,
            serviceProvider?.GetService<IEmbeddingCacheRepository>(),
            serviceProvider?.GetService<IEmbeddingCacheResolver>(),
            serviceProvider?.GetService<IEmbeddingModelProviderResolver>(),
            serviceProvider?.GetService<IRepositoryAiConsentPolicy>()),
        new WhyNotReuseMcpTool(),
        new GetSimilarityClusterMcpTool(),
        new FindReintroducedMcpTool(),
        new ImpactAnalysisMcpTool(),
        new PlanChangeMcpTool(),
        new SafeRefactorMcpTool(),
        new ExplainArchitectureMcpTool(),
        new PreflightChangeMcpTool(mediator),
        new ComposeChangeSummaryMcpTool(),
        new SuggestPlacementMcpTool(),
        new RecordDecisionMcpTool(serviceProvider?.GetService<IHookEventPublisher>()),
        new GetDecisionsMcpTool(),
        new StartSessionMcpTool(serviceProvider?.GetService<IHookEventPublisher>()),
        new EndSessionMcpTool(serviceProvider?.GetService<IHookEventPublisher>()),
        new FlushSummariesMcpTool(serviceProvider?.GetService<IHookEventPublisher>()),
    ]);

    private static bool IsHttpCommand(string[] args) =>
        args.Length is 5 or 6
        && string.Equals(args[0], "http", StringComparison.Ordinal)
        && string.Equals(args[1], "--port", StringComparison.Ordinal)
        && string.Equals(args[3], "--token", StringComparison.Ordinal);
}
