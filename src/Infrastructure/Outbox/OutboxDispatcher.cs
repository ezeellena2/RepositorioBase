using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Infrastructure.Data;

namespace CleanArchitecture.Infrastructure.Outbox;

/// <summary>
/// One pass of the delivery loop, invocable on its own. The hosted service that repeats it adds nothing but the
/// repetition, which is what lets a test drive exactly one iteration against a controlled clock instead of racing
/// a background poller (IA-REQ-028).
/// </summary>
public sealed class OutboxDispatcher(
    ApplicationDbContext context,
    IOutboxSecretReader secrets,
    IEnumerable<IOutboxDeliveryHandler> handlers,
    TimeProvider timeProvider)
{
    private readonly ApplicationDbContext _context = context;
    private readonly IOutboxSecretReader _secrets = secrets;
    private readonly IEnumerable<IOutboxDeliveryHandler> _handlers = handlers;
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <summary>Claims every message due at the current moment and delivers each one once.</summary>
    public Task<int> DispatchDueAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
}

/// <summary>
/// A handler answers for one message type, so dispatch is a lookup rather than a chain of string comparisons
/// copied into every call site.
/// </summary>
public interface IOutboxDeliveryHandler
{
    string MessageType { get; }

    Task<EmailDeliveryReceipt> HandleAsync(Guid outboxMessageId, string payload, string token, CancellationToken cancellationToken);
}
