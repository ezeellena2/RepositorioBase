using CleanArchitecture.Application.Common.Behaviours;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Common.Behaviours;

public sealed class SensitiveRequestLoggingTests
{
    private const string PasswordSentinel = "password-sentinel-7c";
    private const string TokenSentinel = "token-sentinel-7c";
    [Test]
    public async Task Registration_request_is_never_written_to_normal_or_slow_logs()
    {
        var command = new RegisterOrganizationCommand("owner@example.test", PasswordSentinel, "Northwind", "30-12345678-1");
        var records = await ExecuteApplicationLoggingAsync(command);

        AssertSafe(records);
        records.ShouldContain(record => record.Contains(nameof(RegisterOrganizationCommand), StringComparison.Ordinal));
    }

    [Test]
    public async Task Confirmation_request_is_never_written_to_normal_or_slow_logs()
    {
        var command = new ConfirmEmailCommand(TokenSentinel);
        var records = await ExecuteApplicationLoggingAsync(command);

        AssertSafe(records);
        records.ShouldContain(record => record.Contains(nameof(ConfirmEmailCommand), StringComparison.Ordinal));
    }

    private static async Task<IReadOnlyList<string>> ExecuteApplicationLoggingAsync<TRequest>(TRequest request)
        where TRequest : notnull
    {
        var logger = new CapturingLogger<TRequest>();
        await new LoggingBehaviour<TRequest>(logger).Process(request, CancellationToken.None);
        await new PerformanceBehaviour<TRequest, Result>(logger)
            .Handle(request, async _ =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(550));
                return Result.Success();
            }, CancellationToken.None);

        return logger.Records;
    }

    private static void AssertSafe(IEnumerable<string> records)
    {
        var combined = string.Join(Environment.NewLine, records);
        combined.ShouldNotContain(PasswordSentinel);
        combined.ShouldNotContain(TokenSentinel);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _records = [];

        public IReadOnlyList<string> Records => _records;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            _records.Add(state.ToString() ?? string.Empty);
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _records.Add(formatter(state, exception));
            _records.Add(state?.ToString() ?? string.Empty);
            if (exception is not null) _records.Add(exception.ToString());
        }
    }
}
