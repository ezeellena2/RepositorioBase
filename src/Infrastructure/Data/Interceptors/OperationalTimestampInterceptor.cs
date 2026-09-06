using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanArchitecture.Infrastructure.Data.Interceptors;

/// <summary>
/// Stamps when a row was created and last changed, for the aggregates whose timestamps are operational metadata
/// rather than domain state.
/// <para>
/// They are shadow properties rather than aggregate members on purpose. No domain rule depends on them — nothing
/// refuses a transition because of when the last one happened — and the Platform projection needs them only as
/// operational metadata (IA-REQ-044). Putting them on the aggregate would mean threading a clock through every
/// factory and transition to satisfy a reader, which is the tail wagging the dog.
/// </para>
/// </summary>
public sealed class OperationalTimestampInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    internal const string CreatedAt = "CreatedAt";
    internal const string UpdatedAt = "UpdatedAt";

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null) return;
        var now = timeProvider.GetUtcNow();

        Stamp<Tenant>(context, now);
        Stamp<PersonProfile>(context, now);
    }

    private static void Stamp<TEntity>(DbContext context, DateTimeOffset now)
        where TEntity : class
    {
        foreach (var entry in context.ChangeTracker.Entries<TEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property<DateTimeOffset>(CreatedAt).CurrentValue = now;
                entry.Property<DateTimeOffset>(UpdatedAt).CurrentValue = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property<DateTimeOffset>(UpdatedAt).CurrentValue = now;
            }
        }
    }
}
