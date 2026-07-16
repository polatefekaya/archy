using System.Net;

namespace Archy.Features.Web.RunLocalWebHost;

public sealed record LocalWebHostOptions(IPAddress BindAddress, int Port)
{
    public const int DefaultPort = 8788;

    public static bool TryCreate(int port, out LocalWebHostOptions? options)
    {
        options = null;
        if (port is < 1 or > 65535)
        {
            return false;
        }

        options = new LocalWebHostOptions(IPAddress.Loopback, port);
        return true;
    }
}
