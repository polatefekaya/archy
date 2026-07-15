using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Archy.Features.Sidecars.Protocol;

/// <summary>Strict NDJSON codec; a sidecar receives one complete message per line and cannot smuggle a multi-line payload.</summary>
public sealed class SidecarMessageCodec : ISidecarMessageCodec
{
    public byte[] EncodeRequest(SidecarRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return AppendNewline(JsonSerializer.SerializeToUtf8Bytes(request, SidecarProtocolJsonContext.Default.SidecarRequestMessage));
    }

    public byte[] EncodeResponse(SidecarResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return AppendNewline(JsonSerializer.SerializeToUtf8Bytes(response, SidecarProtocolJsonContext.Default.SidecarResponseMessage));
    }

    public bool TryDecodeRequest(ReadOnlySpan<byte> line, out SidecarRequestMessage? request) =>
        TryDecode(line, SidecarProtocolJsonContext.Default.SidecarRequestMessage, out request);

    public bool TryDecodeResponse(ReadOnlySpan<byte> line, out SidecarResponseMessage? response) =>
        TryDecode(line, SidecarProtocolJsonContext.Default.SidecarResponseMessage, out response);

    private static bool TryDecode<T>(ReadOnlySpan<byte> line, JsonTypeInfo<T> typeInfo, out T? value)
    {
        value = default;
        if (line.Length == 0 || line.Length > SidecarProtocolContract.MaximumLineBytes || line.IndexOf((byte)'\n') >= 0 || line.IndexOf((byte)'\r') >= 0)
        {
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize(line, typeInfo);
            return value is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static byte[] AppendNewline(byte[] payload)
    {
        if (payload.Length >= SidecarProtocolContract.MaximumLineBytes)
        {
            throw new InvalidOperationException("Sidecar protocol payload exceeds its line limit.");
        }

        return [.. payload, (byte)'\n'];
    }
}
