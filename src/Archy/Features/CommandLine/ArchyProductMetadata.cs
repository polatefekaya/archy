using System.Reflection;

namespace Archy.Features.CommandLine;

public static class ArchyProductMetadata
{
    public static string Version { get; } = typeof(ArchyProductMetadata).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion ?? "0.1.0-dev";
}
