using Archy.Features.Duplicates.ParseJscpdCloneReport;

namespace Archy.UnitTests.Features.Duplicates.ParseJscpdCloneReport;

public sealed class JscpdCloneReportParserTests
{
    [Fact]
    public void ParseAcceptsNormalizedRangesAndRejectsEscapingPaths()
    {
        var parser = new JscpdCloneReportParser();
        var valid = parser.Parse("""{"toolVersion":"4.0.5","clones":[{"first":{"path":"src/A.cs","startLine":3,"startColumn":1,"endLine":5,"endColumn":1},"second":{"path":"src/B.cs","startLine":4,"startColumn":1,"endLine":6,"endColumn":1},"tokens":19,"lines":3,"format":"csharp"}]}""");
        var escaped = parser.Parse("""{"toolVersion":"4.0.5","clones":[{"first":{"path":"../secret.cs","startLine":3,"startColumn":1,"endLine":5,"endColumn":1},"second":{"path":"src/B.cs","startLine":4,"startColumn":1,"endLine":6,"endColumn":1},"tokens":19,"lines":3,"format":"csharp"}]}""");

        Assert.True(valid.IsSuccess);
        Assert.Equal("src/A.cs", Assert.Single(valid.Value.Clones).First.RepositoryRelativePath);
        Assert.False(escaped.IsSuccess);
    }
}
