using System.Diagnostics;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanArchitecture.Infrastructure.Data.Interceptors;

public sealed class OutboxTraceInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        StampAddedMessages(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        StampAddedMessages(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void StampAddedMessages(DbContext? context)
    {
        var activity = Activity.Current;
        if (context is null || activity is null || activity.IdFormat != ActivityIdFormat.W3C) return;

        var traceId = activity.TraceId.ToString();
        if (traceId.Length != 32) return;

        foreach (var entry in context.ChangeTracker.Entries<OutboxMessage>()
                     .Where(entry => entry.State == EntityState.Added && entry.Entity.TraceId is null))
        {
            entry.Property(nameof(OutboxMessage.TraceId)).CurrentValue = traceId;
        }
    }
}
