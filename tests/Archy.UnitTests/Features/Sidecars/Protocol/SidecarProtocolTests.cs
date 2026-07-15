using Archy.Features.Sidecars.Protocol;

namespace Archy.UnitTests.Features.Sidecars.Protocol;

public sealed class SidecarProtocolTests
{
    [Fact]
    public void ValidatorRejectsHandshakeMismatchAndEscapingPaths()
    {
        var validator = new SidecarProtocolValidator();
        var mismatch = validator.ValidateHandshake(new SidecarHandshakeRequest(1, "1.0.0", [new SidecarCapability("clone", 1)]), new SidecarHandshakeResponse(2, "jscpd", "1.0.0", [new SidecarCapability("clone", 1)]));
        var pathEscape = validator.ValidateRequest(new SidecarRequestMessage(1, "request", "analyze", 1000, ["../secrets"], "{}"));

        Assert.Equal("protocol-version", mismatch!.Code);
        Assert.Equal("invalid-request", pathEscape!.Code);
    }

    [Fact]
    public void CodecUsesOneStrictJsonLine()
    {
        var codec = new SidecarMessageCodec();
        var encoded = codec.EncodeRequest(new SidecarRequestMessage(1, "request", "analyze", 1000, ["src/A.cs"], "{}"));

        Assert.Equal((byte)'\n', encoded[^1]);
        Assert.True(codec.TryDecodeRequest(encoded.AsSpan(0, encoded.Length - 1), out var decoded));
        Assert.Equal("request", decoded!.RequestId);
        Assert.False(codec.TryDecodeRequest(encoded, out _));
    }
}
