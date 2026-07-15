using System.Globalization;
using System.Text;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ExternalLanguageServerProtocol.EstablishStdioSession;

public sealed record LspContentLengthFrameReadResult(bool IsEndOfStream, byte[]? Payload, Problem? Problem)
{
    public static LspContentLengthFrameReadResult EndOfStream() => new(true, null, null);

    public static LspContentLengthFrameReadResult Failure(Problem problem) => new(false, null, problem);

    public static LspContentLengthFrameReadResult Success(byte[] payload) => new(false, payload, null);
}

public static class LspContentLengthFrameCodec
{
    private const int MaximumHeaderBytes = 32 * 1024;

    public static async ValueTask<Problem?> WriteAsync(
        Stream output,
        ReadOnlyMemory<byte> payload,
        int maximumMessageBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (payload.Length is < 0 or > 64 * 1024 * 1024 || payload.Length > maximumMessageBytes)
        {
            return Problem.Validation("Language-server JSON-RPC payload exceeds the configured message limit.");
        }

        var header = Encoding.ASCII.GetBytes($"{LanguageServerProtocolContract.ContentLengthHeaderName}: {payload.Length}{LanguageServerProtocolContract.HeaderTerminator}");
        try
        {
            await output.WriteAsync(header, cancellationToken);
            await output.WriteAsync(payload, cancellationToken);
            await output.FlushAsync(cancellationToken);
            return null;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            return Problem.Storage($"Archy could not write a language-server JSON-RPC frame: {exception.Message}");
        }
    }

    public static async ValueTask<LspContentLengthFrameReadResult> ReadAsync(
        Stream input,
        int maximumMessageBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (maximumMessageBytes is < 1 or > 64 * 1024 * 1024)
        {
            return LspContentLengthFrameReadResult.Failure(Problem.Validation("Language-server message limits must be between one byte and 64 MiB."));
        }

        var header = new List<byte>();
        var oneByte = new byte[1];
        try
        {
            while (true)
            {
                var read = await input.ReadAsync(oneByte, cancellationToken);
                if (read == 0)
                {
                    return header.Count == 0
                        ? LspContentLengthFrameReadResult.EndOfStream()
                        : LspContentLengthFrameReadResult.Failure(Problem.Validation("Language-server JSON-RPC stream ended in a frame header."));
                }

                header.Add(oneByte[0]);
                if (header.Count > MaximumHeaderBytes)
                {
                    return LspContentLengthFrameReadResult.Failure(Problem.Validation("Language-server JSON-RPC header exceeds 32 KiB."));
                }

                if (HasHeaderTerminator(header))
                {
                    break;
                }
            }

            var length = ParseContentLength(header);
            if (!length.IsSuccess)
            {
                return LspContentLengthFrameReadResult.Failure(length.Problem!);
            }

            if (length.Value > maximumMessageBytes)
            {
                return LspContentLengthFrameReadResult.Failure(Problem.Validation("Language-server JSON-RPC frame exceeds the configured message limit."));
            }

            var payload = new byte[length.Value];
            var offset = 0;
            while (offset < payload.Length)
            {
                var read = await input.ReadAsync(payload.AsMemory(offset), cancellationToken);
                if (read == 0)
                {
                    return LspContentLengthFrameReadResult.Failure(Problem.Validation("Language-server JSON-RPC stream ended in a frame payload."));
                }

                offset += read;
            }

            return LspContentLengthFrameReadResult.Success(payload);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            return LspContentLengthFrameReadResult.Failure(Problem.Storage($"Archy could not read a language-server JSON-RPC frame: {exception.Message}"));
        }
    }

    private static bool HasHeaderTerminator(List<byte> header) =>
        header.Count >= 4 &&
        header[^4] == (byte)'\r' &&
        header[^3] == (byte)'\n' &&
        header[^2] == (byte)'\r' &&
        header[^1] == (byte)'\n';

    private static Result<int> ParseContentLength(List<byte> header)
    {
        if (header.Take(header.Count - 4).Any(static value => value > 0x7F))
        {
            return ResultFactory.Failure<int>(Problem.Validation("Language-server JSON-RPC headers must be ASCII."));
        }

        var text = Encoding.ASCII.GetString([.. header.Take(header.Count - 4)]);
        var contentLengthValues = text.Split("\r\n", StringSplitOptions.None)
            .Select(static line => line.Split(':', 2, StringSplitOptions.None))
            .Where(static parts => parts.Length == 2 && string.Equals(parts[0].Trim(), LanguageServerProtocolContract.ContentLengthHeaderName, StringComparison.OrdinalIgnoreCase))
            .Select(static parts => parts[1].Trim())
            .ToArray();

        if (contentLengthValues.Length != 1 ||
            !int.TryParse(contentLengthValues[0], NumberStyles.None, CultureInfo.InvariantCulture, out var contentLength) ||
            contentLength < 0)
        {
            return ResultFactory.Failure<int>(Problem.Validation("Language-server JSON-RPC headers require exactly one non-negative Content-Length."));
        }

        return ResultFactory.Success(contentLength);
    }
}
