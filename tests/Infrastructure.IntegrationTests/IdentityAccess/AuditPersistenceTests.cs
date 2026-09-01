using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

public sealed class AuditPersistenceTests
{
    [Test]
    public async Task AuditEvents_reject_raw_updates()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var auditEvent = await AddAuditEvent(context);

        var exception = Should.Throw<PostgresException>(() => context.Database.ExecuteSql($"UPDATE \"AuditEvents\" SET \"EventType\" = 'changed' WHERE \"Id\" = {auditEvent.Id}"));

        exception.MessageText.ShouldContain("append-only");
    }

    [Test]
    public async Task AuditEvents_reject_tracked_updates()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var auditEvent = await AddAuditEvent(context);

        context.Entry(auditEvent).Property(nameof(AuditEvent.EventType)).CurrentValue = "changed";

        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Test]
    public async Task AuditEvents_reject_raw_deletes()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var auditEvent = await AddAuditEvent(context);

        var exception = Should.Throw<PostgresException>(() => context.Database.ExecuteSql($"DELETE FROM \"AuditEvents\" WHERE \"Id\" = {auditEvent.Id}"));

        exception.MessageText.ShouldContain("append-only");
    }

    [Test]
    public async Task AuditEvents_reject_tracked_deletes()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var auditEvent = await AddAuditEvent(context);

        context.Remove(auditEvent);

        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Test]
    public async Task AuditEvents_reject_truncate()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var exception = Should.Throw<PostgresException>(() => context.Database.ExecuteSqlRaw("TRUNCATE TABLE \"AuditEvents\""));

        exception.MessageText.ShouldContain("append-only");
    }

    private static async Task<AuditEvent> AddAuditEvent(ApplicationDbContext context)
    {
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"audit-{Guid.NewGuid():N}"));
        var auditEvent = AuditEvent.Create(tenant.Id, null, "identity.created", Guid.NewGuid().ToString(), new Dictionary<string, string> { ["outcome"] = "created" });
        context.AddRange(tenant, auditEvent);
        await context.SaveChangesAsync();
        return auditEvent;
    }
}
