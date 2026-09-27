namespace BillingCrm.Application.Common;

public enum ErrorKind
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
}

/// <summary>An expected business failure. Surfaced to clients as ProblemDetails.</summary>
public sealed record Error(
    ErrorKind Kind,
    string Code,
    string Title,
    string Detail,
    IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public static Error Validation(IReadOnlyDictionary<string, string[]> errors) =>
        new(ErrorKind.Validation, "validation", "One or more validation errors occurred.", "See the errors property.", errors);

    public static Error Unauthorized(string code, string title, string detail) =>
        new(ErrorKind.Unauthorized, code, title, detail);

    public static Error Forbidden(string code, string title, string detail) =>
        new(ErrorKind.Forbidden, code, title, detail);

    public static Error NotFound(string code, string title, string detail) =>
        new(ErrorKind.NotFound, code, title, detail);

    public static Error Conflict(string code, string title, string detail) =>
        new(ErrorKind.Conflict, code, title, detail);
}

public class Result
{
    protected Result(Error? error)
    {
        Error = error;
    }

    public bool IsSuccess => Error is null;

    public bool IsFailure => Error is not null;

    /// <summary>Set only when <see cref="IsFailure"/>.</summary>
    public Error? Error { get; }

    public static Result Success() => new(null);

    public static Result Fail(Error error) => new(error);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Fail<T>(Error error) => Result<T>.Fail(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error) : base(error)
    {
        _value = value;
    }

    /// <summary>Only valid when <see cref="Result.IsSuccess"/>; reading it from a failure is a programming error.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");

    public static Result<T> Success(T value) => new(value, null);

    public static new Result<T> Fail(Error error) => new(default, error);
}
