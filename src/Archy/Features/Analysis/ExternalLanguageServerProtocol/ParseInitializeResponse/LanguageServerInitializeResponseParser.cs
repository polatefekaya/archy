using System.Text.Json;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ExternalLanguageServerProtocol.ParseInitializeResponse;

/// <summary>Parses the standard LSP initialize result into Archy's explicit capability profile.</summary>
public static class LanguageServerInitializeResponseParser
{
    public static Result<LanguageServerCapabilityProfile> Parse(
        LanguageServerLaunchSpecification specification,
        JsonElement response)
    {
        ArgumentNullException.ThrowIfNull(specification);
        if (response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("capabilities", out var capabilities) || capabilities.ValueKind != JsonValueKind.Object)
        {
            return ResultFactory.Failure<LanguageServerCapabilityProfile>(Problem.Validation("Language-server initialize response must contain an object result with an object capabilities member."));
        }

        var serverVersion = ParseServerVersion(result);
        var advertised = specification.RequiredCapabilities
            .Select(requirement => Advertise(requirement.Name, capabilities))
            .ToArray();
        return ResultFactory.Success(LanguageServerCapabilityNegotiator.Negotiate(specification, serverVersion, advertised));
    }

    private static LanguageServerVersionReport ParseServerVersion(JsonElement result)
    {
        if (!result.TryGetProperty("serverInfo", out var serverInfo) || serverInfo.ValueKind != JsonValueKind.Object)
        {
            return new LanguageServerVersionReport(null, null);
        }

        return new LanguageServerVersionReport(
            OptionalString(serverInfo, "name"),
            OptionalString(serverInfo, "version"));
    }

    private static LanguageServerCapabilityAdvertisement Advertise(string capabilityName, JsonElement capabilities)
    {
        var property = capabilityName switch
        {
            "documentSymbol" => "documentSymbolProvider",
            "definition" => "definitionProvider",
            "references" => "referencesProvider",
            "callHierarchy" => "callHierarchyProvider",
            "typeDefinition" => "typeDefinitionProvider",
            _ => null,
        };
        if (property is null)
        {
            return new LanguageServerCapabilityAdvertisement(capabilityName, false, "Archy has no standard-LSP capability mapping for this requirement.");
        }

        if (!capabilities.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.False)
        {
            return new LanguageServerCapabilityAdvertisement(capabilityName, false, $"The server did not advertise '{property}'.");
        }

        return value.ValueKind is JsonValueKind.True or JsonValueKind.Object
            ? new LanguageServerCapabilityAdvertisement(capabilityName, true, null)
            : new LanguageServerCapabilityAdvertisement(capabilityName, false, $"The server advertised an invalid '{property}' capability value.");
    }

    private static string? OptionalString(JsonElement objectValue, string propertyName) =>
        objectValue.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
