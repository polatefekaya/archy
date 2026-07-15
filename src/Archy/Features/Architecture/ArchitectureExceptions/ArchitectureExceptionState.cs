namespace Archy.Features.Architecture.ArchitectureExceptions;

/// <summary>Whether an exception can suppress its exact target at the current verification instant.</summary>
public enum ArchitectureExceptionState
{
    Active,
    ReviewDue,
    Expired,
    Unused,
}
