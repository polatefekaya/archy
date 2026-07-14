using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Workspaces.LocateWorkspace;

public sealed record LocateWorkspaceCommand(string StartPath) : IRequest<Result<LocatedWorkspace>>;
