using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Analysis.InventorySources;

public sealed record InventoryWorkspaceSourcesCommand(
    string StartPath,
    string? ExplicitConfigurationPath,
    string? StateRootOverride)
    : IRequest<Result<SourceInventory>>;
