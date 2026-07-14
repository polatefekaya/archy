using Archy.Features.CommandLine;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Storage.InitializeWorkspaceDatabase;
using Archy.Features.Storage.AnalysisRuns;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.Features.Workspaces.InitializeWorkspace;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSingleton<IWorkspaceLocator, WorkspaceLocator>();
services.AddSingleton<IWorkspaceStateLayout, WorkspaceStateLayout>();
services.AddSingleton<TimeProvider>(TimeProvider.System);
services.AddSingleton<IWorkspaceManifestStore, WorkspaceManifestStore>();
services.AddSingleton<IWorkspaceLockManager, WorkspaceLockManager>();
services.AddSingleton<IWorkspaceDatabaseInitializer, WorkspaceDatabaseInitializer>();
services.AddSingleton<IAnalysisRunStore, AnalysisRunStore>();
services.AddSingleton<IGraphRevisionStore, GraphRevisionStore>();
services.AddSingleton<IUserConfigurationPathProvider, UserConfigurationPathProvider>();
services.AddSingleton<ITomlConfigurationParser, TomlConfigurationParser>();
services.AddSingleton<IArchyConfigurationLoader, ArchyConfigurationLoader>();
services.AddMediator(options =>
{
    options.Namespace = "Archy.Generated";
    options.ServiceLifetime = ServiceLifetime.Singleton;
    options.Assemblies = [typeof(LocateWorkspaceCommand).Assembly];
});

using var serviceProvider = services.BuildServiceProvider();
var mediator = serviceProvider.GetRequiredService<IMediator>();

return await ArchyCli.RunAsync(args, mediator, CancellationToken.None);
