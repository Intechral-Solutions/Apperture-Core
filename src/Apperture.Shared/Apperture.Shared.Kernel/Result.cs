namespace Apperture.Shared.Kernel;

public static class Result
{
    public static Result<T> Ok<T>(T value) => new(value, null);
    public static Result<T> Fail<T>(string error) => new(default, error ?? throw new ArgumentNullException(nameof(error)));
}

public sealed class Result<T>
{
    readonly T? _value;
    readonly string? _error;

    internal Result(T? value, string? error)
    {
        _value = value;
        _error = error;
    }

    public bool IsSuccess => _error is null;
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException(_error);
    public string? Error => _error;

    public static implicit operator Result<T>(T value) => Result.Ok(value);
}
