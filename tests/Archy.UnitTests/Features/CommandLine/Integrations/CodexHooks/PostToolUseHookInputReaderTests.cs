using Archy.Features.CommandLine.Integrations.CodexHooks;
using Archy.Features.Integrations.Codex.PostToolChangedPaths;

namespace Archy.UnitTests.Features.CommandLine.Integrations.CodexHooks;

public sealed class PostToolUseHookInputReaderTests
{
    [Theory]
    [InlineData("Edit", PostToolKind.Edit)]
    [InlineData("MultiEdit", PostToolKind.Edit)]
    [InlineData("NotebookEdit", PostToolKind.Edit)]
    [InlineData("apply_patch", PostToolKind.Edit)]
    [InlineData("Write", PostToolKind.Write)]
    [InlineData("Bash", PostToolKind.Bash)]
    public async Task ReadMapsEveryFileMutatingToolOfEverySupportedHost(string toolName, PostToolKind expected)
    {
        var input = await ReadAsync(
            "{\"session_id\":\"s1\",\"cwd\":\"/repo\",\"tool_name\":\"" + toolName + "\",\"tool_input\":{\"file_path\":\"src/A.cs\"}}");

        Assert.NotNull(input);
        Assert.Equal(expected, input!.ToolKind);
        Assert.Equal(["src/A.cs"], input.ReportedPaths);
    }

    [Fact]
    public async Task ReadCollectsTheNotebookPathReportedByNotebookEdit()
    {
        var input = await ReadAsync("""
            {"session_id":"s1","cwd":"/repo","tool_name":"NotebookEdit","tool_input":{"notebook_path":"analysis/report.ipynb"}}
            """);

        Assert.NotNull(input);
        Assert.Equal(["analysis/report.ipynb"], input!.ReportedPaths);
    }

    [Fact]
    public async Task ReadKeepsTheFilePathReportedByMultiEditAcrossSeveralEdits()
    {
        var input = await ReadAsync("""
            {"session_id":"s1","cwd":"/repo","tool_name":"MultiEdit","tool_input":{"file_path":"src/B.cs","edits":[{"old_string":"a","new_string":"b"},{"old_string":"c","new_string":"d"}]}}
            """);

        Assert.NotNull(input);
        Assert.Equal(PostToolKind.Edit, input!.ToolKind);
        Assert.Equal(["src/B.cs"], input.ReportedPaths);
    }

    [Fact]
    public async Task ReadRejectsAToolThatDoesNotMutateFiles()
    {
        var input = await ReadAsync("""
            {"session_id":"s1","cwd":"/repo","tool_name":"Read","tool_input":{"file_path":"src/A.cs"}}
            """);

        Assert.Null(input);
    }

    [Fact]
    public async Task ReadRejectsInputWithoutAWorkingDirectory()
    {
        var input = await ReadAsync("""
            {"session_id":"s1","tool_name":"Edit","tool_input":{"file_path":"src/A.cs"}}
            """);

        Assert.Null(input);
    }

    private static async Task<PostToolUseHookInput?> ReadAsync(string json)
    {
        var original = Console.In;
        try
        {
            Console.SetIn(new StringReader(json));
            return await PostToolUseHookInputReader.ReadAsync(CancellationToken.None);
        }
        finally
        {
            Console.SetIn(original);
        }
    }
}
