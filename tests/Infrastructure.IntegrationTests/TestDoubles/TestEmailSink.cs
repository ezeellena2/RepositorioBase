using System.Collections.Concurrent;
using CleanArchitecture.Application.Common.Interfaces;

namespace CleanArchitecture.Infrastructure.IntegrationTests.TestDoubles;

/// <summary>
/// The isolated sink IA-REQ-018 allows in place of a provider. It records every attempt so a test can ask what
/// was sent, how often, and under which idempotency key — the three questions a delivery loop has to answer.
/// </summary>
public sealed class TestEmailSink : IIdentityEmailSender
{
    private readonly ConcurrentQueue<SentEmail> _sent = new();
    private readonly ConcurrentDictionary<string, string> _receiptsByKey = new();

    /// <summary>What the next send answers. A test sets it to model a transient or a permanent provider failure.</summary>
    public Func<string, EmailDeliveryReceipt>? Respond { get; set; }

    /// <summary>Thrown instead of answering, to model a provider that fails without a receipt at all.</summary>
    public Exception? Throw { get; set; }
    public Func<Task>? BeforeSend { get; set; }
    public int AcceptedCount => _receiptsByKey.Count;

    public IReadOnlyList<SentEmail> Sent => _sent.ToArray();

    public async Task<EmailDeliveryReceipt> SendAsync(string recipient, string subject, string body, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (BeforeSend is { } before) await before();
        if (Throw is { } failure)
        {
            throw failure;
        }

        _sent.Enqueue(new SentEmail(recipient, subject, body, idempotencyKey));
        if (Respond is { } respond)
        {
            return respond(idempotencyKey);
        }

        // A provider that has already accepted this key answers with the same receipt, which is what makes a
        // retry after an acknowledged send reconcilable instead of a second delivery.
        var receipt = _receiptsByKey.GetOrAdd(idempotencyKey, key => $"receipt-{key}");
        return new EmailDeliveryReceipt(true, receipt, false);
    }

    public sealed record SentEmail(string Recipient, string Subject, string Body, string IdempotencyKey);
}

/// <summary>A clock a test moves by hand, so backoff is asserted rather than waited for.</summary>
public sealed class ControlledTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan value) => _now = _now.Add(value);
}
