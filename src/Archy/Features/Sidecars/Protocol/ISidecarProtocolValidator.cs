namespace Archy.Features.Sidecars.Protocol;

public interface ISidecarProtocolValidator
{
    SidecarProtocolFailure? ValidateHandshake(SidecarHandshakeRequest request, SidecarHandshakeResponse response);

    SidecarProtocolFailure? ValidateRequest(SidecarRequestMessage request);

    SidecarProtocolFailure? ValidateResponse(SidecarResponseMessage response);
}
