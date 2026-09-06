using CleanArchitecture.Domain.Entities;

using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
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

    DbSet<PendingRegistrationIntent> PendingRegistrationIntents { get; }

    DbSet<PendingPersonalIntent> PendingPersonalIntents { get; }

    DbSet<PersonProfile> PersonProfiles { get; }

    DbSet<PersonalTenantOwnership> PersonalTenantOwnerships { get; }

    DbSet<IdentityDocument> IdentityDocuments { get; }

    DbSet<OutboxMessage> OutboxMessages { get; }

    DbSet<OutboxSecret> OutboxSecrets { get; }

    DbSet<UserSession> UserSessions { get; }

    DbSet<RecentIdentityProof> RecentIdentityProofs { get; }

    DbSet<CleanArchitecture.Domain.IdentityAccess.Credentials.PasswordResetRequest> PasswordResetRequests { get; }

    DbSet<IdentitySecurityState> IdentitySecurityStates { get; }

    DbSet<Invitation> Invitations { get; }

    DbSet<InvitationRole> InvitationRoles { get; }

    DbSet<PlatformAdminInvitation> PlatformAdminInvitations { get; }

    DbSet<PlatformMfaEnrollment> PlatformMfaEnrollments { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Replaces a tracked entity's values with the committed row so a handler that lost an optimistic update can
    /// re-decide against the state that actually won, instead of retrying stale values.
    /// </summary>
    Task ReloadAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class;
}
