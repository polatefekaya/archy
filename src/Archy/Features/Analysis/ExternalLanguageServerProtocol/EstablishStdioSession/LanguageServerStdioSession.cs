using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Archy.SharedKernel.Primitives;
using Archy.Features.Analysis.ExternalLanguageServerProtocol.ParseInitializeResponse;

namespace Archy.Features.Analysis.ExternalLanguageServerProtocol.EstablishStdioSession;

public sealed class LanguageServerStdioSession : ILanguageServerSession
{
    private readonly LanguageServerLaunchSpecification specification;
    private readonly Process process;
    private readonly CancellationTokenSource sessionCancellation = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<Result<JsonDocument>>> pendingRequests = new();
    private readonly ConcurrentDictionary<long, byte> canceledRequests = new();
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly object diagnosticsGate = new();
    private readonly List<LanguageServerTransportDiagnostic> diagnostics = [];
    private readonly List<byte> standardError = [];
    private Task? responseReader;
    private Task? standardErrorReader;
    private Problem? terminalProblem;
    private bool standardErrorWasTruncated;
    private int nextRequestId;
    private int shutdownStarted;
    private int disposed;
    private LanguageServerCapabilityProfile capabilityProfile;

    private LanguageServerStdioSession(LanguageServerLaunchSpecification specification, Process process)
    {
        this.specification = specification;
        this.process = process;
        capabilityProfile = new LanguageServerCapabilityProfile(new LanguageServerVersionReport(null, null), []);
    }

    public LanguageServerCapabilityProfile CapabilityProfile => capabilityProfile;

    public static async ValueTask<Result<LanguageServerStdioSession>> StartAsync(
        LanguageServerLaunchSpecification specification,
        int? clientProcessId,
        CancellationToken cancellationToken)
    {
        var validation = LanguageServerProtocolContract.ValidateLaunchSpecification(specification);
        if (validation is not null)
        {
            return ResultFactory.Failure<LanguageServerStdioSession>(validation);
        }

        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo(specification.Command)
            {
                WorkingDirectory = specification.RepositoryRoot,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in specification.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                process.Dispose();
                return ResultFactory.Failure<LanguageServerStdioSession>(Problem.Storage($"Archy could not start language server '{specification.ServerId}'."));
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            process?.Dispose();
            return ResultFactory.Failure<LanguageServerStdioSession>(Problem.Storage($"Archy could not start language server '{specification.ServerId}': {exception.Message}"));
        }

        var session = new LanguageServerStdioSession(specification, process!);
        session.StartReaders();
        var initialize = LanguageServerProtocolContract.CreateInitializeRequest(specification, clientProcessId);
        if (!initialize.IsSuccess)
        {
            await session.DisposeAsync();
            return ResultFactory.Failure<LanguageServerStdioSession>(initialize.Problem!);
        }

        var parameters = JsonSerializer.SerializeToElement(
            initialize.Value.Parameters,
            LanguageServerProtocolJsonContext.Default.LspInitializeParameters);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(specification.Timeouts.InitializeMilliseconds);
        var initialized = await session.SendRequestAsync(initialize.Value.Method, parameters, timeout.Token);
        if (!initialized.IsSuccess)
        {
            await session.DisposeAsync();
            return ResultFactory.Failure<LanguageServerStdioSession>(initialized.Problem!);
        }

        using (initialized.Value)
        {
            var profile = LanguageServerInitializeResponseParser.Parse(specification, initialized.Value.RootElement);
            if (!profile.IsSuccess)
            {
                await session.DisposeAsync();
                return ResultFactory.Failure<LanguageServerStdioSession>(profile.Problem!);
            }

            session.capabilityProfile = profile.Value;
        }

        var notificationProblem = await session.SendNotificationAsync("initialized", null, cancellationToken);
        if (notificationProblem is not null)
        {
            await session.DisposeAsync();
            return ResultFactory.Failure<LanguageServerStdioSession>(notificationProblem);
        }

        return ResultFactory.Success(session);
    }

    public async ValueTask<Result<JsonDocument>> SendRequestAsync(
        string method,
        JsonElement? parameters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            return ResultFactory.Failure<JsonDocument>(Problem.Validation("Language-server JSON-RPC requests require a method."));
        }

        var fault = GetTerminalProblem();
        if (fault is not null)
        {
            return ResultFactory.Failure<JsonDocument>(fault);
        }

        if (Volatile.Read(ref disposed) != 0 && Volatile.Read(ref shutdownStarted) == 0)
        {
            return ResultFactory.Failure<JsonDocument>(Problem.Conflict("Language-server session is already disposed."));
        }

        var requestId = Interlocked.Increment(ref nextRequestId);
        var completion = new TaskCompletionSource<Result<JsonDocument>>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pendingRequests.TryAdd(requestId, completion))
        {
            return ResultFactory.Failure<JsonDocument>(Problem.Conflict("Language-server JSON-RPC request ID collision."));
        }

        var payload = CreateMessage(requestId, method, parameters);
        Problem? writeProblem;
        try
        {
            writeProblem = await WriteAsync(payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            pendingRequests.TryRemove(requestId, out _);
            return ResultFactory.Failure<JsonDocument>(Problem.Conflict($"Language-server request '{method}' was cancelled before it was sent."));
        }
        if (writeProblem is not null)
        {
            pendingRequests.TryRemove(requestId, out _);
            return ResultFactory.Failure<JsonDocument>(writeProblem);
        }

        try
        {
            return await completion.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (pendingRequests.TryRemove(requestId, out _))
            {
                canceledRequests.TryAdd(requestId, 0);
                var cancellationProblem = await SendCancellationNotificationAsync(requestId);
                if (cancellationProblem is not null)
                {
                    AddDiagnostic("lsp_cancel_notification_failed", cancellationProblem.Message);
                }
            }

            return ResultFactory.Failure<JsonDocument>(Problem.Conflict($"Language-server request '{method}' was cancelled."));
        }
    }

    public async ValueTask<Problem?> SendNotificationAsync(
        string method,
        JsonElement? parameters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            return Problem.Validation("Language-server JSON-RPC notifications require a method.");
        }

        var fault = GetTerminalProblem();
        if (fault is not null)
        {
            return fault;
        }

        try
        {
            return await WriteAsync(CreateMessage(null, method, parameters), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Problem.Conflict($"Language-server notification '{method}' was cancelled before it was sent.");
        }
    }

    public async ValueTask<Problem?> ShutdownAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref shutdownStarted, 1) != 0)
        {
            return null;
        }

        var fault = GetTerminalProblem();
        if (fault is null)
        {
            var shutdown = await SendRequestAsync("shutdown", null, cancellationToken);
            if (!shutdown.IsSuccess)
            {
                return shutdown.Problem;
            }

            shutdown.Value.Dispose();
            var exitProblem = await SendNotificationAsync("exit", null, cancellationToken);
            if (exitProblem is not null)
            {
                return exitProblem;
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(specification.Timeouts.ShutdownMilliseconds);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return null;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            TerminateProcess();
            return Problem.Storage($"Language server '{specification.ServerId}' did not exit within the configured shutdown timeout.");
        }
    }

    public LanguageServerStdioSessionDiagnostics GetDiagnostics()
    {
        lock (diagnosticsGate)
        {
            return new LanguageServerStdioSessionDiagnostics([.. diagnostics], Encoding.UTF8.GetString([.. standardError]), standardErrorWasTruncated);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        if (!process.HasExited && Volatile.Read(ref shutdownStarted) == 0 && GetTerminalProblem() is null)
        {
            using var timeout = new CancellationTokenSource(specification.Timeouts.ShutdownMilliseconds);
            _ = await ShutdownAsync(timeout.Token);
        }

        sessionCancellation.Cancel();
        TerminateProcess();
        await AwaitReadersAsync();
        FailPending(Problem.Conflict("Language-server session was disposed."));
        writeGate.Dispose();
        sessionCancellation.Dispose();
        process.Dispose();
    }

    private void StartReaders()
    {
        responseReader = ReadResponsesAsync();
        standardErrorReader = CaptureStandardErrorAsync();
    }

    private async Task ReadResponsesAsync()
    {
        try
        {
            while (true)
            {
                var frame = await LspContentLengthFrameCodec.ReadAsync(
                    process.StandardOutput.BaseStream,
                    specification.MaximumMessageBytes,
                    sessionCancellation.Token);
                if (frame.IsEndOfStream)
                {
                    if (Volatile.Read(ref shutdownStarted) == 0 && Volatile.Read(ref disposed) == 0)
                    {
                        FailSession(Problem.Storage($"Language server '{specification.ServerId}' closed its JSON-RPC stream."));
                    }

                    return;
                }

                if (frame.Problem is not null)
                {
                    AddDiagnostic("lsp_malformed_frame", frame.Problem.Message);
                    FailSession(frame.Problem);
                    return;
                }

                await HandleMessageAsync(frame.Payload!);
            }
        }
        catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            FailSession(Problem.Storage($"Archy could not read language-server responses: {exception.Message}"));
        }
    }

    private async Task HandleMessageAsync(byte[] payload)
    {
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("jsonrpc", out var version) ||
                !string.Equals(version.GetString(), LanguageServerProtocolContract.JsonRpcVersion, StringComparison.Ordinal))
            {
                AddDiagnostic("lsp_malformed_response", "Language-server message is missing JSON-RPC version.");
                FailSession(Problem.Validation("Language-server JSON-RPC message is malformed."));
                return;
            }

            if (root.TryGetProperty("method", out var method))
            {
                await HandleServerMethodAsync(root, method.GetString());
                return;
            }

            if (!root.TryGetProperty("id", out var id) || !id.TryGetInt64(out var requestId))
            {
                AddDiagnostic("lsp_malformed_response", "Language-server response is missing a numeric response ID.");
                FailSession(Problem.Validation("Language-server JSON-RPC response is malformed."));
                return;
            }

            if (canceledRequests.TryRemove(requestId, out _))
            {
                return;
            }

            if (!pendingRequests.TryRemove(requestId, out var pending))
            {
                AddDiagnostic("lsp_unmatched_response", $"Language-server response ID '{requestId}' has no pending request.");
                return;
            }

            if (root.TryGetProperty("error", out var error))
            {
                pending.TrySetResult(ResultFactory.Failure<JsonDocument>(Problem.Conflict($"Language-server request failed: {ErrorMessage(error)}")));
                return;
            }

            if (!root.TryGetProperty("result", out _))
            {
                pending.TrySetResult(ResultFactory.Failure<JsonDocument>(Problem.Validation("Language-server JSON-RPC response has neither result nor error.")));
                return;
            }

            pending.TrySetResult(ResultFactory.Success(document));
            document = null;
        }
        catch (JsonException exception)
        {
            AddDiagnostic("lsp_malformed_response", exception.Message);
            FailSession(Problem.Validation("Language-server emitted invalid JSON-RPC JSON."));
        }
        finally
        {
            document?.Dispose();
        }
    }

    private async Task HandleServerMethodAsync(JsonElement message, string? method)
    {
        if (!message.TryGetProperty("id", out var id))
        {
            AddDiagnostic("lsp_server_notification", $"Language server sent notification '{method ?? "unknown"}'.");
            return;
        }

        if (!id.TryGetInt64(out var requestId))
        {
            AddDiagnostic("lsp_malformed_server_request", "Language-server request has a non-numeric request ID.");
            FailSession(Problem.Validation("Language-server request is malformed."));
            return;
        }

        AddDiagnostic("lsp_server_request_unsupported", $"Language server requested unsupported client method '{method ?? "unknown"}'.");
        var responseProblem = await WriteAsync(CreateMethodNotFoundResponse(requestId), CancellationToken.None);
        if (responseProblem is not null)
        {
            FailSession(responseProblem);
        }
    }

    private async Task CaptureStandardErrorAsync()
    {
        var buffer = new byte[1024];
        try
        {
            while (true)
            {
                var read = await process.StandardError.BaseStream.ReadAsync(buffer, sessionCancellation.Token);
                if (read == 0)
                {
                    return;
                }

                lock (diagnosticsGate)
                {
                    var remaining = specification.MaximumStandardErrorBytes - standardError.Count;
                    if (remaining <= 0)
                    {
                        standardErrorWasTruncated = true;
                        continue;
                    }

                    standardError.AddRange(buffer[..Math.Min(read, remaining)]);
                    standardErrorWasTruncated |= read > remaining;
                }
            }
        }
        catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            AddDiagnostic("lsp_stderr_capture_failed", exception.Message);
        }
    }

    private async ValueTask<Problem?> WriteAsync(byte[] payload, CancellationToken cancellationToken)
    {
        try
        {
            await writeGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }

        try
        {
            var problem = await LspContentLengthFrameCodec.WriteAsync(
                process.StandardInput.BaseStream,
                payload,
                specification.MaximumMessageBytes,
                cancellationToken);
            if (problem is not null)
            {
                FailSession(problem);
            }

            return problem;
        }
        finally
        {
            writeGate.Release();
        }
    }

    private async ValueTask<Problem?> SendCancellationNotificationAsync(long requestId)
    {
        var payload = CreateMessage(null, "$/cancelRequest", CreateCancellationParameters(requestId));
        return await WriteAsync(payload, CancellationToken.None);
    }

    private static JsonElement CreateCancellationParameters(long requestId)
    {
        using var document = JsonDocument.Parse($"{{\"id\":{requestId}}}");
        return document.RootElement.Clone();
    }

    private static byte[] CreateMessage(long? requestId, string method, JsonElement? parameters)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", LanguageServerProtocolContract.JsonRpcVersion);
            if (requestId is not null)
            {
                writer.WriteNumber("id", requestId.Value);
            }

            writer.WriteString("method", method);
            if (parameters is not null)
            {
                writer.WritePropertyName("params");
                parameters.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static byte[] CreateMethodNotFoundResponse(long requestId)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", LanguageServerProtocolContract.JsonRpcVersion);
            writer.WriteNumber("id", requestId);
            writer.WriteStartObject("error");
            writer.WriteNumber("code", -32601);
            writer.WriteString("message", "Archy does not support server-initiated JSON-RPC requests in lsp-process/v1.");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static string ErrorMessage(JsonElement error) =>
        error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message)
            ? message.GetString() ?? "Unspecified language-server error."
            : "Unspecified language-server error.";

    private Problem? GetTerminalProblem()
    {
        lock (diagnosticsGate)
        {
            return terminalProblem;
        }
    }

    private void FailSession(Problem problem)
    {
        lock (diagnosticsGate)
        {
            terminalProblem ??= problem;
        }

        FailPending(problem);
    }

    private void FailPending(Problem problem)
    {
        foreach (var pending in pendingRequests)
        {
            if (pendingRequests.TryRemove(pending.Key, out var completion))
            {
                completion.TrySetResult(ResultFactory.Failure<JsonDocument>(problem));
            }
        }
    }

    private void AddDiagnostic(string code, string message)
    {
        lock (diagnosticsGate)
        {
            diagnostics.Add(new LanguageServerTransportDiagnostic(code, message));
        }
    }

    private void TerminateProcess()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AddDiagnostic("lsp_process_termination_failed", exception.Message);
        }
    }

    private async Task AwaitReadersAsync()
    {
        var readers = new[] { responseReader, standardErrorReader }.Where(static reader => reader is not null).Cast<Task>().ToArray();
        if (readers.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(readers);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            AddDiagnostic("lsp_reader_shutdown_failed", exception.Message);
        }
    }
}
