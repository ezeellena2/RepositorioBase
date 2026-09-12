using System.Data.Common;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Application.Common.Logging;

/// <summary>Safe operational facts about an unexpected exception.</summary>
public sealed record SafeFailure(
    string Operation,
    string TraceId,
    string ExceptionTypes,
    string StackFrames,
    string? SqlState)
{
    private const int MaximumExceptionDepth = 16;
    private const int MaximumStackFrames = 12;

    /// <summary>
    /// Describes an unexpected exception without reading its message, data, source, help link or raw stack text.
    /// <paramref name="operation"/> must be a code-owned operation name or endpoint display name, never request data.
    /// </summary>
    public static SafeFailure Describe(Exception exception, string operation, string? traceId = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var chain = ExceptionChain(exception).ToArray();
        return new SafeFailure(
            operation,
            traceId ?? Activity.Current?.TraceId.ToString() ?? "none",
            string.Join(" <- ", chain.Select(item => SafeIdentity(item.GetType().FullName ?? item.GetType().Name))),
            StackFrameIdentities(exception),
            SqlStateFrom(chain));
    }

    private static IEnumerable<Exception> ExceptionChain(Exception exception)
    {
        var current = exception;
        for (var depth = 0; current is not null && depth < MaximumExceptionDepth; depth++)
        {
            yield return current;
            current = current.InnerException;
        }
    }

    private static string StackFrameIdentities(Exception exception)
    {
        var frames = new StackTrace(exception, false).GetFrames() ?? [];
        return string.Join(
            " < ",
            frames
                .Select(frame => frame.GetMethod())
                .Where(method => method is not null)
                .Take(MaximumStackFrames)
                .Select(method => SafeIdentity($"{method!.DeclaringType?.FullName ?? "unknown"}.{method.Name}"))
                .Where(identity => identity.Length > 0));
    }

    private static string? SqlStateFrom(IEnumerable<Exception> chain)
    {
        foreach (var databaseException in chain.OfType<DbException>())
        {
            string? sqlState;
            try
            {
                sqlState = databaseException.SqlState;
            }
            catch
            {
                // A diagnostic accessor must never replace the original failure or become a second failure.
                continue;
            }

            if (sqlState is { Length: 5 } && sqlState.All(character => char.IsAsciiLetterOrDigit(character)))
            {
                return sqlState;
            }
        }

        return null;
    }

    private static string SafeIdentity(string value) =>
        new(value.Where(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '+' or '`' or '<' or '>').ToArray());
}

public static class SafeFailureLoggingExtensions
{
    /// <summary>Writes the safe projection only. The exception object is deliberately not an argument.</summary>
    public static void LogSafeFailure(this ILogger logger, SafeFailure failure) =>
        logger.LogError(
            "Unexpected failure (unexpected_failure) in {Operation}; TraceId {TraceId}; ExceptionTypes {ExceptionTypes}; SqlState {SqlState}; StackFrames {StackFrames}",
            failure.Operation,
            failure.TraceId,
            failure.ExceptionTypes,
            failure.SqlState,
            failure.StackFrames);
}
