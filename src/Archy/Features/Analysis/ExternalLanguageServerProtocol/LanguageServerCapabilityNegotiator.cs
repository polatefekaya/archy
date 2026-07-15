namespace Archy.Features.Analysis.ExternalLanguageServerProtocol;

public static class LanguageServerCapabilityNegotiator
{
    public static LanguageServerCapabilityProfile Negotiate(
        LanguageServerLaunchSpecification specification,
        LanguageServerVersionReport serverVersion,
        IReadOnlyList<LanguageServerCapabilityAdvertisement> advertisedCapabilities)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(serverVersion);
        ArgumentNullException.ThrowIfNull(advertisedCapabilities);

        var advertised = advertisedCapabilities
            .Where(static capability => !string.IsNullOrWhiteSpace(capability.Name))
            .GroupBy(static capability => capability.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);

        var statuses = specification.RequiredCapabilities
            .OrderBy(static requirement => requirement.Name, StringComparer.Ordinal)
            .Select(requirement => ToStatus(requirement, advertised.GetValueOrDefault(requirement.Name)))
            .ToArray();

        return new LanguageServerCapabilityProfile(serverVersion, statuses);
    }

    private static LanguageServerCapabilityStatus ToStatus(
        LanguageServerCapabilityRequirement requirement,
        LanguageServerCapabilityAdvertisement? advertisement)
    {
        if (advertisement?.IsAvailable == true)
        {
            return new LanguageServerCapabilityStatus(requirement.Name, LanguageServerCapabilityState.Available, advertisement.Detail);
        }

        var detail = advertisement?.Detail ?? "The server did not advertise this capability.";
        return new LanguageServerCapabilityStatus(
            requirement.Name,
            requirement.RequiredForSemanticAnalysis ? LanguageServerCapabilityState.Degraded : LanguageServerCapabilityState.Unavailable,
            detail);
    }
}
