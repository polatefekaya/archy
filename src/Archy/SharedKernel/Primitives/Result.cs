namespace Archy.SharedKernel.Primitives;

public readonly struct Result<T>
{
    private readonly T? value;

    internal Result(T value)
    {
        this.value = value;
        Problem = null;
        IsSuccess = true;
    }

    internal Result(Problem problem)
    {
        value = default;
        Problem = problem ?? throw new ArgumentNullException(nameof(problem));
        IsSuccess = false;
    }

    public bool IsSuccess { get; }

    public Problem? Problem { get; }

    public T Value => IsSuccess
        ? value!
        : throw new InvalidOperationException("A failed result does not have a value.");

}

public static class ResultFactory
{
    public static Result<T> Success<T>(T value) => new(value);

    public static Result<T> Failure<T>(Problem problem) => new(problem);
}
