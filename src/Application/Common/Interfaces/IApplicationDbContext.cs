using CleanArchitecture.Domain.Entities;

using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<TodoList> TodoLists { get; }

    DbSet<TodoItem> TodoItems { get; }

    DbSet<Tenant> Tenants { get; }

    DbSet<OrganizationProfile> OrganizationProfiles { get; }

    DbSet<TenantMembership> TenantMemberships { get; }

    DbSet<AuditEvent> AuditEvents { get; }

    DbSet<RegistrationSubmission> RegistrationSubmissions { get; }

    DbSet<OutboxMessage> OutboxMessages { get; }

    DbSet<OutboxSecret> OutboxSecrets { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
