using Archy.Features.Duplicates.DuplicateFindings;

namespace Archy.Features.Duplicates.AggregateDuplicateSignals;

/// <summary>Explicit per-signal qualification floors; a finding still requires at least two independent qualified signals.</summary>
public sealed record DuplicateSignalThresholds(double Structural, double Signature, double Semantic)
{
    public static DuplicateSignalThresholds Default { get; } = new(.70d, .75d, .80d);

    public double For(DuplicateSignalKind kind) => kind switch
    {
        DuplicateSignalKind.Structural => Structural,
        DuplicateSignalKind.Signature => Signature,
        DuplicateSignalKind.Semantic => Semantic,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown duplicate signal kind."),
    };
}
