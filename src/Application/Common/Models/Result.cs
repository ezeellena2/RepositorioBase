namespace CleanArchitecture.Application.Common.Models;

public class Result
{
    protected Result(bool succeeded, ApplicationError? error)
    {
        if (succeeded == (error is not null))
        {
            throw new ArgumentException("A result must contain either success or exactly one error.", nameof(error));
        }

        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }

    public bool IsSuccess => Succeeded;

    public bool IsFailure => !Succeeded;

    public ApplicationError? Error { get; }

    public static Result Success()
    {
        return new Result(true, null);
    }

    public static Result Failure(ApplicationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(false, error);
    }
}

public sealed class Result<T> : Result
{
    private Result(T? value, ApplicationError? error, bool succeeded)
        : base(succeeded, error)
    {
        Value = value;
    }

    public T? Value { get; }

    public static Result<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<T>(value, null, true);
    }

    public static new Result<T> Failure(ApplicationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(default, error, false);
    }
}
