namespace Archy.Features.Analysis.CSharpPatternTables;

public sealed record CSharpPatternTable(
    string SchemaVersion,
    IReadOnlyList<CSharpPatternDefinition> Patterns);

public sealed record CSharpPatternDefinition(
    string Id,
    string Framework,
    CSharpPatternMatchKind MatchKind,
    string Member,
    string? TypeConstraint,
    CSharpPatternCapture? Capture);

public sealed record CSharpPatternCapture(string Name, int ArgumentIndex);

public enum CSharpPatternMatchKind
{
    Invocation,
    Type,
    Attribute,
}
