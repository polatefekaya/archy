using Archy.SharedKernel.Primitives;
using Mediator;
namespace Archy.Features.Diagnostics.ReadConfiguredLanguages;
public sealed record ReadConfiguredLanguagesQuery(string Path, string? ConfigurationPath, string? StateRoot) : IRequest<Result<IReadOnlyList<ConfiguredLanguageProfileStatus>>>;
