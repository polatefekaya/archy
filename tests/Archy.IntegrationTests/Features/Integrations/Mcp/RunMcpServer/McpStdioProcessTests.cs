using System.Text.Json;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Integrations.Mcp.RunMcpServer;

public sealed class McpStdioProcessTests
{
    private static readonly string[] ExpectedToolNames =
    [
        "check_duplicate",
        "check_violation",
        "compose_change_summary",
        "end_session",
        "explain_architecture",
        "find_reintroduced",
        "find_similar",
        "flush_summaries",
        "get_decisions",
        "get_dependents",
        "get_doctor_readiness",
        "get_module_rules",
        "get_similarity_cluster",
        "impact_analysis",
        "plan_change",
        "preflight_change",
        "record_decision",
        "resolve_symbol",
        "safe_refactor",
        "start_session",
        "suggest_placement",
        "why_not_reuse",
    ];

    [Fact]
    public async Task HostReportsTheActualWorkspaceFailureOnStandardError()
    {
        using var repository = TemporaryRepository.Create();
        var host = Path.Combine(AppContext.BaseDirectory, "Archy.dll");
        var missing = Path.Combine(repository.Root, "does-not-exist");

        var result = await LocalProcess.RunAsync("dotnet", repository.Root, standardInput: null, host, "mcp", "stdio", missing);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Contains("Reason", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("not_found", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleasedHostKeepsStdoutProtocolCleanAndExecutesATool()
    {
        using var repository = TemporaryRepository.Create();
        var home = Path.Combine(Path.GetTempPath(), $"archy-mcp-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);
        try
        {
            var host = Path.Combine(AppContext.BaseDirectory, "Archy.dll");
            Assert.True(File.Exists(host));
            var input = """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"integration-test","version":"1.0"}}}
                {"jsonrpc":"2.0","method":"notifications/initialized","params":{}}
                {"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
                {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"find_similar","arguments":{"query":"session lifecycle","limit":5}}}
                {"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get_doctor_readiness","arguments":{}}}
                """;

            var result = await LocalProcess.RunAsync(
                "dotnet", repository.Root, input, new Dictionary<string, string> { ["HOME"] = home }, host, "mcp", "stdio");

            Assert.Equal(0, result.ExitCode);
            var responses = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            Assert.Equal(4, responses.Length);
            using (responses[0])
            {
                Assert.Equal("archy", responses[0].RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
            }
            using (responses[1])
            {
                Assert.Equal(22, responses[1].RootElement.GetProperty("result").GetProperty("tools").GetArrayLength());
                Assert.Contains(responses[1].RootElement.GetProperty("result").GetProperty("tools").EnumerateArray(), tool => tool.GetProperty("name").GetString() == "find_similar");
                Assert.Contains(responses[1].RootElement.GetProperty("result").GetProperty("tools").EnumerateArray(), tool => tool.GetProperty("name").GetString() == "get_doctor_readiness");
                Assert.Contains(responses[1].RootElement.GetProperty("result").GetProperty("tools").EnumerateArray(), tool => tool.GetProperty("name").GetString() == "plan_change");
                Assert.Contains(responses[1].RootElement.GetProperty("result").GetProperty("tools").EnumerateArray(), tool => tool.GetProperty("name").GetString() == "explain_architecture");
                Assert.Contains(responses[1].RootElement.GetProperty("result").GetProperty("tools").EnumerateArray(), tool => tool.GetProperty("name").GetString() == "preflight_change");
                Assert.Contains(responses[1].RootElement.GetProperty("result").GetProperty("tools").EnumerateArray(), tool => tool.GetProperty("name").GetString() == "compose_change_summary");
            }
            using (responses[2])
            {
                Assert.True(responses[2].RootElement.GetProperty("result").GetProperty("content").GetArrayLength() > 0);
            }
            using (responses[3])
            {
                var readiness = responses[3].RootElement.GetProperty("result").GetProperty("structuredContent");
                Assert.True(readiness.GetProperty("checks").GetArrayLength() > 0);
                Assert.True(readiness.TryGetProperty("mutatedWorkingTree", out var mutated)); Assert.False(mutated.GetBoolean());
            }
            Assert.Contains("MCP READY", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task ReleasedHostPublishesValidContractsAndExecutesEveryMcpTool()
    {
        using var repository = TemporaryRepository.Create();
        using var home = TemporaryDirectory.Create("mcp-all-tools-home");
        var environment = new Dictionary<string, string> { ["HOME"] = home.Path };
        var host = Path.Combine(AppContext.BaseDirectory, "Archy.dll");
        Assert.True(File.Exists(host));

        Directory.CreateDirectory(Path.Combine(repository.Root, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(repository.Root, "src", "SmokeTarget.cs"),
            "namespace Smoke; public sealed class SmokeTarget { public void Run() { } }");
        await File.WriteAllTextAsync(
            Path.Combine(repository.Root, "archy.toml"),
            """
            schema_version = 1

            [[layers]]
            name = "Product"
            include = ["src/**/*.cs"]
            may_depend_on = []
            """);

        var initialized = await LocalProcess.RunAsync(
            "dotnet",
            repository.Root,
            standardInput: null,
            environment,
            host,
            "workspace",
            "init",
            "--path",
            repository.Root,
            "--json");
        Assert.Equal(0, initialized.ExitCode);

        var analyzed = await LocalProcess.RunAsync(
            "dotnet",
            repository.Root,
            standardInput: null,
            environment,
            host,
            "analyze",
            "--path",
            repository.Root,
            "--json");
        Assert.Equal(0, analyzed.ExitCode);

        var located = new WorkspaceLocator().Locate(repository.Root);
        Assert.True(located.IsSuccess);
        var stateRoot = Path.Combine(home.Path, ".archy", "workspaces");
        var location = new WorkspaceStateLayout().Resolve(located.Value, stateRoot);
        Assert.True(location.IsSuccess);
        var snapshot = await new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System))
            .ReadActiveAsync(location.Value, CancellationToken.None);
        Assert.True(snapshot.IsSuccess);
        Assert.NotNull(snapshot.Value);
        var target = Assert.Single(
            snapshot.Value.Nodes,
            static node => node.CanonicalKey == "csharp:type:Smoke.SmokeTarget");

        var targetJson = JsonSerializer.Serialize(target.StableId);
        const string sessionId = "mcp-all-tools-session";
        var validCalls = new (string Name, string Arguments)[]
        {
            ("check_violation", "{}"),
            ("get_dependents", $$"""{"stableId":{{targetJson}},"depth":1}"""),
            ("resolve_symbol", """{"query":"SmokeTarget","limit":5}"""),
            ("get_doctor_readiness", "{}"),
            ("get_module_rules", $$"""{"stableId":{{targetJson}}}"""),
            ("check_duplicate", $$"""{"stableId":{{targetJson}}}"""),
            ("find_similar", """{"query":"smoke target","limit":5}"""),
            ("why_not_reuse", $$"""{"candidateStableId":{{targetJson}},"proposedStableId":{{targetJson}},"intent":"reuse smoke target"}"""),
            ("get_similarity_cluster", """{"clusterId":"missing-cluster"}"""),
            ("find_reintroduced", $$"""{"stableId":{{targetJson}}}"""),
            ("impact_analysis", $$"""{"stableId":{{targetJson}},"direction":"both","depth":2,"maxNodes":50}"""),
            ("plan_change", $$"""{"description":"extend smoke target","targetStableId":{{targetJson}},"intendedFiles":["src/SmokeTarget.cs"]}"""),
            ("safe_refactor", $$"""{"stableId":{{targetJson}},"intent":"rename"}"""),
            ("explain_architecture", $$"""{"lookup":{{targetJson}},"historyDepth":1}"""),
            ("start_session", $$"""{"actorKind":"agent","actorId":"integration-test","sessionId":"{{sessionId}}","clientKind":"codex"}"""),
            ("preflight_change", $$"""{"description":"extend smoke target","intendedFiles":["src/SmokeTarget.cs"],"targetStableIds":[{{targetJson}}],"sessionId":"{{sessionId}}"}"""),
            ("compose_change_summary", $$"""{"baseRevision":{{snapshot.Value.Revision}}}"""),
            ("suggest_placement", $$"""{"dependencies":[{"targetStableId":{{targetJson}},"confidence":1.0}]}"""),
            ("record_decision", $$"""{"decisionType":"smoke","resolution":"accepted","actorKind":"agent","actorId":"integration-test","targets":[{"kind":"graph_node","stableId":{{targetJson}}}],"sessionId":"{{sessionId}}","graphRevision":{{snapshot.Value.Revision}}}"""),
            ("get_decisions", string.Concat("{\"target\":{\"kind\":\"graph_node\",\"stableId\":", targetJson, "}}")),
            ("flush_summaries", $$"""{"sessionId":"{{sessionId}}"}"""),
            ("end_session", $$"""{"sessionId":"{{sessionId}}"}"""),
        };
        Assert.Equal(ExpectedToolNames.Order(StringComparer.Ordinal), validCalls.Select(static call => call.Name).Order(StringComparer.Ordinal));
        var toolsRequiringArguments = ExpectedToolNames
            .Except(["check_violation", "get_doctor_readiness"], StringComparer.Ordinal)
            .ToArray();

        var requests = new List<string>
        {
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"all-tools-test","version":"1.0"}}}""",
            """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""",
            """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""",
        };
        for (var index = 0; index < validCalls.Length; index++)
        {
            requests.Add(ToolCall(index + 10, validCalls[index].Name, validCalls[index].Arguments));
        }
        for (var index = 0; index < toolsRequiringArguments.Length; index++)
        {
            requests.Add(ToolCall(index + 100, toolsRequiringArguments[index], "{}"));
        }

        var result = await LocalProcess.RunAsync(
            "dotnet",
            repository.Root,
            string.Join('\n', requests),
            environment,
            host,
            "mcp",
            "stdio");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("MCP READY", result.StandardError, StringComparison.Ordinal);
        var responses = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => JsonDocument.Parse(line))
            .ToArray();
        try
        {
            Assert.Equal(validCalls.Length + toolsRequiringArguments.Length + 2, responses.Length);
            var byId = responses.ToDictionary(
                static response => response.RootElement.GetProperty("id").GetInt32());
            var tools = byId[2].RootElement.GetProperty("result").GetProperty("tools");
            Assert.Equal(ExpectedToolNames, tools.EnumerateArray().Select(static tool => tool.GetProperty("name").GetString()));
            foreach (var tool in tools.EnumerateArray())
            {
                Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("description").GetString()));
                var schema = tool.GetProperty("inputSchema");
                Assert.Equal(JsonValueKind.Object, schema.ValueKind);
                Assert.Equal("object", schema.GetProperty("type").GetString());
                Assert.Equal(JsonValueKind.Object, schema.GetProperty("properties").ValueKind);
            }

            for (var index = 0; index < validCalls.Length; index++)
            {
                var response = byId[index + 10].RootElement;
                Assert.False(
                    response.TryGetProperty("error", out _),
                    $"{validCalls[index].Name}: {response.GetRawText()}");
                var toolResult = response.GetProperty("result");
                Assert.NotEmpty(toolResult.GetProperty("content").EnumerateArray());
                Assert.Equal(JsonValueKind.Object, toolResult.GetProperty("structuredContent").ValueKind);
            }

            for (var index = 0; index < toolsRequiringArguments.Length; index++)
            {
                var response = byId[index + 100].RootElement;
                Assert.True(
                    response.TryGetProperty("error", out var error),
                    $"{toolsRequiringArguments[index]} accepted an empty argument object.");
                Assert.Equal(-32003, error.GetProperty("code").GetInt32());
                Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
            }
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    private static string ToolCall(int id, string name, string arguments)
    {
        using var parsedArguments = JsonDocument.Parse(arguments);
        return JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            method = "tools/call",
            @params = new
            {
                name,
                arguments = parsedArguments.RootElement,
            },
        });
    }
}
