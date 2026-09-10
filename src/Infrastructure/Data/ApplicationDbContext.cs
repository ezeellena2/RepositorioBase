using System.Reflection;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<OrganizationProfile> OrganizationProfiles => Set<OrganizationProfile>();

    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<RegistrationSubmission> RegistrationSubmissions => Set<RegistrationSubmission>();

    public DbSet<PendingRegistrationIntent> PendingRegistrationIntents => Set<PendingRegistrationIntent>();

    public DbSet<PendingPersonalIntent> PendingPersonalIntents => Set<PendingPersonalIntent>();

    public DbSet<PersonProfile> PersonProfiles => Set<PersonProfile>();

    public DbSet<PersonalTenantOwnership> PersonalTenantOwnerships => Set<PersonalTenantOwnership>();

    public DbSet<IdentityDocument> IdentityDocuments => Set<IdentityDocument>();

    public DbSet<IdentityDocumentFingerprint> IdentityDocumentFingerprints => Set<IdentityDocumentFingerprint>();

    public DbSet<CleanArchitecture.Infrastructure.IdentityAccess.Security.IdentityAttemptBudget> IdentityAttemptBudgets => Set<CleanArchitecture.Infrastructure.IdentityAccess.Security.IdentityAttemptBudget>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<OutboxSecret> OutboxSecrets => Set<OutboxSecret>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<RecentIdentityProof> RecentIdentityProofs => Set<RecentIdentityProof>();

    public DbSet<CleanArchitecture.Domain.IdentityAccess.Credentials.PasswordResetRequest> PasswordResetRequests => Set<CleanArchitecture.Domain.IdentityAccess.Credentials.PasswordResetRequest>();

    public DbSet<CleanArchitecture.Domain.IdentityAccess.Identities.AccountReactivationRequest> AccountReactivationRequests => Set<CleanArchitecture.Domain.IdentityAccess.Identities.AccountReactivationRequest>();

    public DbSet<CleanArchitecture.Domain.IdentityAccess.Retention.RetentionLegalHold> RetentionLegalHolds => Set<CleanArchitecture.Domain.IdentityAccess.Retention.RetentionLegalHold>();

    public DbSet<CleanArchitecture.Domain.IdentityAccess.Retention.PersonalDataErasureRecord> PersonalDataErasureRecords => Set<CleanArchitecture.Domain.IdentityAccess.Retention.PersonalDataErasureRecord>();

    public DbSet<IdentityDocumentDispute> IdentityDocumentDisputes => Set<IdentityDocumentDispute>();

    public DbSet<IdentityDocumentCorrectionRecord> IdentityDocumentCorrectionRecords => Set<IdentityDocumentCorrectionRecord>();

    public DbSet<CleanArchitecture.Domain.IdentityAccess.ExternalLogins.ExternalAuthorizationRequest> ExternalAuthorizationRequests => Set<CleanArchitecture.Domain.IdentityAccess.ExternalLogins.ExternalAuthorizationRequest>();

    public DbSet<IdentitySecurityState> IdentitySecurityStates => Set<IdentitySecurityState>();

    public DbSet<Role> TenantRoles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<PlatformAdminInvitation> PlatformAdminInvitations => Set<PlatformAdminInvitation>();

    public DbSet<PlatformMfaEnrollment> PlatformMfaEnrollments => Set<PlatformMfaEnrollment>();

    public DbSet<PlatformRecoveryCode> PlatformRecoveryCodes => Set<PlatformRecoveryCode>();

    public DbSet<InvitationRole> InvitationRoles => Set<InvitationRole>();

    public DbSet<MembershipRole> MembershipRoles => Set<MembershipRole>();

    public Task ReloadAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class =>
        Entry(entity).ReloadAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
