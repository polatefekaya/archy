namespace Archy.Features.Duplicates.EvaluateDuplicateDetection;

/// <summary>Labelled, reviewable C# pair used to prevent duplicate-detection threshold drift.</summary>
public sealed record DuplicateEvaluationCase(
    string Id,
    string Category,
    string LeftSource,
    string RightSource,
    DuplicateEvaluationExpectation Expected);
