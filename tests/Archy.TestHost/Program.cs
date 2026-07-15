using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Analysis.ExternalLanguageServerProtocol;
using Archy.Features.Analysis.ExternalLanguageServerProtocol.EstablishStdioSession;
using System.Text.Json;

return args switch
{
    ["workspace-lock-worker", ..] => await WorkspaceLockWorker.RunAsync(args[1..]),
    ["lsp-mock-server", ..] => await LanguageServerMock.RunAsync(args[1..]),
    ["lsp-client-fixture", ..] => await LanguageServerNativeClientFixture.RunAsync(args[1..]),
    _ => 64,
};

internal static class WorkspaceLockWorker
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 4 ||
            !Enum.TryParse<WorkspaceLockMode>(args[1], ignoreCase: true, out var mode) ||
            !int.TryParse(args[3], out var holdMilliseconds) ||
            holdMilliseconds < 0)
        {
            return 64;
        }

        var stateDirectory = args[0];
        var outcomePath = args[2];
        var location = new WorkspaceStateLocation(
            "lock-worker",
            stateDirectory,
            Path.Combine(stateDirectory, "workspace.json"),
            Path.Combine(stateDirectory, "locks", "workspace.lock"),
            Path.Combine(stateDirectory, "archy.db"));
        var result = await new WorkspaceLockManager(TimeProvider.System).AcquireAsync(
            location,
            mode,
            TimeSpan.FromMilliseconds(250),
            CancellationToken.None);

        await File.WriteAllTextAsync(outcomePath, result.IsSuccess ? "acquired" : result.Problem!.Code);
        if (!result.IsSuccess)
        {
            return 0;
        }

        using var lease = result.Value;
        if (holdMilliseconds > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(holdMilliseconds));
        }

        return 0;
    }
}

internal static class LanguageServerNativeClientFixture
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 2 || !File.Exists(args[0]) || !Directory.Exists(args[1]))
        {
            return 64;
        }

        var mockServerAssembly = Path.GetFullPath(args[0]);
        var specification = new LanguageServerLaunchSpecification(
            LanguageServerProtocolContract.CurrentSchemaVersion,
            "archy-test-host",
            "csharp",
            "dotnet",
            [mockServerAssembly, "lsp-mock-server"],
            Path.GetFullPath(args[1]),
            new LanguageServerTimeouts(5_000, 5_000, 5_000),
            new LanguageServerRestartPolicy(0, 0),
            1_048_576,
            65_536,
            [new LanguageServerCapabilityRequirement("definition", true)]);
        var started = await LanguageServerStdioSession.StartAsync(specification, Environment.ProcessId, CancellationToken.None);
        if (!started.IsSuccess)
        {
            await Console.Error.WriteAsync(started.Problem!.Message);
            return 1;
        }

        await using var session = started.Value;
        using var parameters = JsonDocument.Parse("{\"fixture\":true}");
        var response = await session.SendRequestAsync("test/echo", parameters.RootElement.Clone(), CancellationToken.None);
        if (!response.IsSuccess)
        {
            await Console.Error.WriteAsync(response.Problem!.Message);
            return 1;
        }

        using (response.Value)
        {
            if (response.Value.RootElement.GetProperty("result").GetProperty("echoRequestId").GetInt64() != 2)
            {
                return 1;
            }
        }

        var shutdown = await session.ShutdownAsync(CancellationToken.None);
        if (shutdown is not null)
        {
            await Console.Error.WriteAsync(shutdown.Message);
            return 1;
        }

        await Console.Out.WriteAsync("native-lsp-client-ok\n");
        return 0;
    }
}

internal static class LanguageServerMock
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args is not ([] or ["delay"] or ["semantic"] or ["semantic-relations"]))
        {
            return 64;
        }

        var delayResponses = args is ["delay"];
        var semanticResponses = args is ["semantic"];
        var semanticRelationshipResponses = args is ["semantic-relations"];
        string? rootUri = null;
        await Console.Error.WriteAsync("mock-lsp-ready\n");
        var input = Console.OpenStandardInput();
        var output = Console.OpenStandardOutput();
        while (true)
        {
            var payload = await ReadFrameAsync(input);
            if (payload is null)
            {
                return 0;
            }

            using var document = System.Text.Json.JsonDocument.Parse(payload);
            var root = document.RootElement;
            var method = root.GetProperty("method").GetString();
            if (string.Equals(method, "initialize", StringComparison.Ordinal))
            {
                rootUri = root.GetProperty("params").GetProperty("rootUri").GetString();
                var capabilities = semanticRelationshipResponses
                    ? "{\"documentSymbolProvider\":true,\"referencesProvider\":true,\"callHierarchyProvider\":true}"
                    : semanticResponses ? "{\"documentSymbolProvider\":true}" : "{}";
                await WriteResultAsync(output, root.GetProperty("id").GetInt64(), $"{{\"capabilities\":{capabilities},\"serverInfo\":{{\"name\":\"archy-mock-lsp\",\"version\":\"1.0\"}}}}");
                await WriteNotificationAsync(output, "window/logMessage");
                continue;
            }

            if (semanticResponses && string.Equals(method, "textDocument/documentSymbol", StringComparison.Ordinal))
            {
                await WriteResultAsync(output, root.GetProperty("id").GetInt64(), "[{\"name\":\"Clock\",\"kind\":5,\"selectionRange\":{\"start\":{\"line\":1,\"character\":20},\"end\":{\"line\":1,\"character\":25}}}]");
                continue;
            }

            if (semanticRelationshipResponses && string.Equals(method, "textDocument/documentSymbol", StringComparison.Ordinal))
            {
                await WriteResultAsync(output, root.GetProperty("id").GetInt64(), SemanticRelationshipDocumentSymbols);
                continue;
            }

            if (semanticRelationshipResponses && string.Equals(method, "textDocument/references", StringComparison.Ordinal))
            {
                var line = root.GetProperty("params").GetProperty("position").GetProperty("line").GetInt32();
                var references = line == 3
                    ? $"[{{\"uri\":{JsonSerializer.Serialize(DocumentUri(rootUri, "src/clock.ts"))},\"range\":{{\"start\":{{\"line\":1,\"character\":2}},\"end\":{{\"line\":1,\"character\":8}}}}}}]"
                    : "[]";
                await WriteResultAsync(output, root.GetProperty("id").GetInt64(), references);
                continue;
            }

            if (semanticRelationshipResponses && string.Equals(method, "textDocument/prepareCallHierarchy", StringComparison.Ordinal))
            {
                var line = root.GetProperty("params").GetProperty("position").GetProperty("line").GetInt32();
                var prepared = line == 0
                    ? $"[{CallHierarchyItem("Caller", rootUri, 0, 16, 0, 22, 0, 0, 2, 1)}]"
                    : "[]";
                await WriteResultAsync(output, root.GetProperty("id").GetInt64(), prepared);
                continue;
            }

            if (semanticRelationshipResponses && string.Equals(method, "callHierarchy/outgoingCalls", StringComparison.Ordinal))
            {
                var callerName = root.GetProperty("params").GetProperty("item").GetProperty("name").GetString();
                var outgoing = string.Equals(callerName, "Caller", StringComparison.Ordinal)
                    ? $"[{{\"to\":{CallHierarchyItem("Target", rootUri, 3, 16, 3, 22, 3, 0, 3, 26)},\"fromRanges\":[{{\"start\":{{\"line\":1,\"character\":2}},\"end\":{{\"line\":1,\"character\":8}}}}]}}]"
                    : "[]";
                await WriteResultAsync(output, root.GetProperty("id").GetInt64(), outgoing);
                continue;
            }

            if (string.Equals(method, "test/echo", StringComparison.Ordinal))
            {
                var requestId = root.GetProperty("id").GetInt64();
                await WriteResultAsync(output, requestId, $"{{\"echoRequestId\":{requestId}}}");
                continue;
            }

            if (delayResponses && string.Equals(method, "test/delay", StringComparison.Ordinal))
            {
                var requestId = root.GetProperty("id").GetInt64();
                await Task.Delay(250);
                await WriteResultAsync(output, requestId, $"{{\"echoRequestId\":{requestId}}}");
                continue;
            }

            if (string.Equals(method, "shutdown", StringComparison.Ordinal))
            {
                await WriteResultAsync(output, root.GetProperty("id").GetInt64(), "null");
                continue;
            }

            if (string.Equals(method, "exit", StringComparison.Ordinal))
            {
                return 0;
            }
        }
    }

    private const string SemanticRelationshipDocumentSymbols = """
        [{"name":"Caller","kind":6,"range":{"start":{"line":0,"character":0},"end":{"line":2,"character":1}},"selectionRange":{"start":{"line":0,"character":16},"end":{"line":0,"character":22}}},{"name":"Target","kind":6,"range":{"start":{"line":3,"character":0},"end":{"line":3,"character":26}},"selectionRange":{"start":{"line":3,"character":16},"end":{"line":3,"character":22}}}]
        """;

    private static string CallHierarchyItem(string name, string? rootUri, int selectionStartLine, int selectionStartCharacter, int selectionEndLine, int selectionEndCharacter, int rangeStartLine, int rangeStartCharacter, int rangeEndLine, int rangeEndCharacter) =>
        $"{{\"name\":{JsonSerializer.Serialize(name)},\"kind\":6,\"uri\":{JsonSerializer.Serialize(DocumentUri(rootUri, "src/clock.ts"))},\"range\":{{\"start\":{{\"line\":{rangeStartLine},\"character\":{rangeStartCharacter}}},\"end\":{{\"line\":{rangeEndLine},\"character\":{rangeEndCharacter}}}}},\"selectionRange\":{{\"start\":{{\"line\":{selectionStartLine},\"character\":{selectionStartCharacter}}},\"end\":{{\"line\":{selectionEndLine},\"character\":{selectionEndCharacter}}}}}}}";

    private static string DocumentUri(string? rootUri, string relativePath) =>
        new Uri(new Uri(rootUri ?? throw new InvalidOperationException("Mock LSP was not initialized.")), relativePath).AbsoluteUri;

    private static async Task<byte[]?> ReadFrameAsync(Stream input)
    {
        var header = new List<byte>();
        var oneByte = new byte[1];
        while (true)
        {
            var read = await input.ReadAsync(oneByte);
            if (read == 0)
            {
                return header.Count == 0 ? null : throw new InvalidOperationException("Mock LSP received an incomplete header.");
            }

            header.Add(oneByte[0]);
            if (header.Count >= 4 && header[^4] == '\r' && header[^3] == '\n' && header[^2] == '\r' && header[^1] == '\n')
            {
                break;
            }
        }

        var headerText = System.Text.Encoding.ASCII.GetString([.. header]);
        var contentLength = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Single(static line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            .Split(':', 2)[1];
        var payload = new byte[int.Parse(contentLength, System.Globalization.CultureInfo.InvariantCulture)];
        var offset = 0;
        while (offset < payload.Length)
        {
            var read = await input.ReadAsync(payload.AsMemory(offset));
            if (read == 0)
            {
                throw new InvalidOperationException("Mock LSP received an incomplete payload.");
            }

            offset += read;
        }

        return payload;
    }

    private static async Task WriteResultAsync(Stream output, long requestId, string result)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes($"{{\"jsonrpc\":\"2.0\",\"id\":{requestId},\"result\":{result}}}");
        var header = System.Text.Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
        await output.WriteAsync(header);
        await output.WriteAsync(payload);
        await output.FlushAsync();
    }

    private static async Task WriteNotificationAsync(Stream output, string method)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes($"{{\"jsonrpc\":\"2.0\",\"method\":\"{method}\"}}");
        var header = System.Text.Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
        await output.WriteAsync(header);
        await output.WriteAsync(payload);
        await output.FlushAsync();
    }
}
