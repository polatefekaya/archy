namespace Archy.Features.Sidecars.Protocol;

public static class SidecarProtocolContract
{
    public const int Version = 1;
    public const int MaximumLineBytes = 1_000_000;
    public static readonly TimeSpan MaximumRequestTimeout = TimeSpan.FromMinutes(5);
}
