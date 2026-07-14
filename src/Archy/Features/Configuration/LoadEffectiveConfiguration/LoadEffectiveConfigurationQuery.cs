using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Configuration.LoadEffectiveConfiguration;

public sealed record LoadEffectiveConfigurationQuery(
    string StartPath,
    string? ExplicitConfigurationPath,
    string? StateRootOverride)
    : IRequest<Result<EffectiveConfiguration>>;
