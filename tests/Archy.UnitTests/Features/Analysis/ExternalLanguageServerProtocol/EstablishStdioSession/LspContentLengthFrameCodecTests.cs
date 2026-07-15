using System.Text;
using Archy.Features.Analysis.ExternalLanguageServerProtocol.EstablishStdioSession;

namespace Archy.UnitTests.Features.Analysis.ExternalLanguageServerProtocol.EstablishStdioSession;

public sealed class LspContentLengthFrameCodecTests
{
    [Fact]
    public async Task RoundTripsUtf8PayloadWithContentLengthFraming()
    {
        var stream = new MemoryStream();
        var payload = Encoding.UTF8.GetBytes("{\"message\":\"İstanbul\"}");

        var written = await LspContentLengthFrameCodec.WriteAsync(stream, payload, 1024, CancellationToken.None);
        stream.Position = 0;
        var read = await LspContentLengthFrameCodec.ReadAsync(stream, 1024, CancellationToken.None);

        Assert.Null(written);
        Assert.Null(read.Problem);
        Assert.False(read.IsEndOfStream);
        Assert.Equal(payload, read.Payload);
    }

    [Theory]
    [InlineData("Content-Length: 2\r\nContent-Length: 2\r\n\r\n{}")]
    [InlineData("Content-Length: nope\r\n\r\n{}")]
    public async Task RejectsAmbiguousOrInvalidContentLength(string frame)
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes(frame));

        var read = await LspContentLengthFrameCodec.ReadAsync(stream, 1024, CancellationToken.None);

        Assert.NotNull(read.Problem);
    }

    [Fact]
    public async Task RejectsPayloadThatExceedsConfiguredLimit()
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes("Content-Length: 3\r\n\r\n{} "));

        var read = await LspContentLengthFrameCodec.ReadAsync(stream, 2, CancellationToken.None);

        Assert.NotNull(read.Problem);
    }
}
