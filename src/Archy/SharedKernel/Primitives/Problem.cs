namespace Archy.SharedKernel.Primitives;

public sealed record Problem(string Code, string Message)
{
    public static Problem Validation(string message) => new("validation", message);

    public static Problem NotFound(string message) => new("not_found", message);

    public static Problem Conflict(string message) => new("conflict", message);

    public static Problem Storage(string message) => new("storage_error", message);
}
