using System.Data.Common;
using CleanArchitecture.Application.Common.Logging;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Common.Logging;

public sealed class SafeFailureTests
{
    private const string FailureMessage = "password=message-must-not-be-logged";
    private const string ProviderMessage = "document=27182818 must not be logged";

    [Test]
    public void Description_and_log_keep_only_safe_failure_facts()
    {
        var exception = CaptureNestedFailure("23505");

        var failure = SafeFailure.Describe(exception, "outbox.dispatch", "trace-safe-17");
        var logger = new CapturingLogger();
        logger.LogSafeFailure(failure);

        failure.Operation.ShouldBe("outbox.dispatch");
        failure.TraceId.ShouldBe("trace-safe-17");
        failure.ExceptionTypes.ShouldContain(typeof(InvalidOperationException).FullName!);
        failure.ExceptionTypes.ShouldContain(typeof(ClassifiedDbException).FullName!);
        failure.StackFrames.ShouldContain(nameof(ThrowNestedFailure));
        failure.SqlState.ShouldBe("23505");

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeNull("the raw exception must never be handed to a logging provider");
        entry.Text.ShouldContain("unexpected_failure");
        entry.Text.ShouldContain("trace-safe-17");
        entry.Text.ShouldContain("23505");
        AssertContainsNoPrivateFailureText(failure.ToString());
        AssertContainsNoPrivateFailureText(entry.Text);
    }

    [Test]
    public void Provider_classification_is_dropped_when_it_is_not_a_sql_state()
    {
        var failure = SafeFailure.Describe(
            CaptureNestedFailure("provider-secret-instead-of-a-code"),
            "lifecycle.maintenance",
            "trace-safe-18");

        failure.SqlState.ShouldBeNull();
        AssertContainsNoPrivateFailureText(failure.ToString());
        failure.ToString().ShouldNotContain("provider-secret-instead-of-a-code");
    }

    private static Exception CaptureNestedFailure(string sqlState)
    {
        try
        {
            ThrowNestedFailure(sqlState);
            throw new AssertionException("The test failure was not thrown.");
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    private static void ThrowNestedFailure(string sqlState) =>
        throw new InvalidOperationException(FailureMessage, new ClassifiedDbException(ProviderMessage, sqlState));

    private static void AssertContainsNoPrivateFailureText(string value)
    {
        value.ShouldNotContain(FailureMessage);
        value.ShouldNotContain(ProviderMessage);
        value.ShouldNotContain("27182818");
    }

    private sealed class ClassifiedDbException(string message, string sqlState) : DbException(message)
    {
        public override string? SqlState { get; } = sqlState;
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    private sealed record LogEntry(LogLevel Level, string Text, Exception? Exception);
}
