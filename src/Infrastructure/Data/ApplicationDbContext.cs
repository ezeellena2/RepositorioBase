using System.Reflection;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
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

    public DbSet<TodoList> TodoLists => Set<TodoList>();

    public DbSet<TodoItem> TodoItems => Set<TodoItem>();

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<OrganizationProfile> OrganizationProfiles => Set<OrganizationProfile>();

    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<RegistrationSubmission> RegistrationSubmissions => Set<RegistrationSubmission>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<OutboxSecret> OutboxSecrets => Set<OutboxSecret>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<Role> TenantRoles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

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
