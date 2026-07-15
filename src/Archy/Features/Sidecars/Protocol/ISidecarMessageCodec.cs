namespace Archy.Features.Sidecars.Protocol;

public interface ISidecarMessageCodec
{
    byte[] EncodeRequest(SidecarRequestMessage request);

    byte[] EncodeResponse(SidecarResponseMessage response);

    bool TryDecodeRequest(ReadOnlySpan<byte> line, out SidecarRequestMessage? request);

    bool TryDecodeResponse(ReadOnlySpan<byte> line, out SidecarResponseMessage? response);
}
