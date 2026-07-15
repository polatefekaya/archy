namespace Archy.Features.Memory.ModelProviders.Contracts;

public sealed record ModelProviderResult<T>(T? Value, ModelProviderFailure? Failure)
{
    public bool IsSuccess => Failure is null && Value is not null;
}

public static class ModelProviderResults
{
    public static ModelProviderResult<T> Success<T>(T value) =>
        new(value ?? throw new ArgumentNullException(nameof(value)), null);

    public static ModelProviderResult<T> Fail<T>(ModelProviderFailure failure) =>
        new(default, failure ?? throw new ArgumentNullException(nameof(failure)));
}
