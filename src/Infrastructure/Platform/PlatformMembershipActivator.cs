using CleanArchitecture.Application.IdentityAccess.Platform;
using CleanArchitecture.Application.IdentityAccess.Platform.Mfa;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.Platform;

/// <summary>
/// Attaches an existing Platform system role to a membership. It lives here because it is the only thing in the
/// Platform slice that touches the tenant authorization tables, which the application context deliberately does
/// not expose.
/// </summary>
public sealed class PlatformMembershipActivator(ApplicationDbContext context) : IPlatformMembershipActivator
{
    public async Task<bool> TryAssignSystemRoleAsync(Tenant platform, TenantMembership membership, string roleName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(membership);
        var normalized = roleName.ToUpperInvariant();

        var role = await context.TenantRoles.SingleOrDefaultAsync(
            candidate => candidate.TenantId == platform.Id && candidate.NormalizedName == normalized && candidate.IsSystem && !candidate.IsRetired,
            cancellationToken);
        if (role is null)
        {
            return false;
        }

        context.MembershipRoles.Add(MembershipRole.Create(platform, membership, role));
        return true;
    }

    public async Task<bool> IsLastActiveOwnerAsync(Tenant platform, TenantMembership membership, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(membership);
        var owner = PlatformRoles.Owner.ToUpperInvariant();

        var holdsOwner = await context.MembershipRoles.AnyAsync(
            assignment => assignment.MembershipId == membership.Id &&
                          context.TenantRoles.Any(role => role.Id == assignment.RoleId && role.NormalizedName == owner),
            cancellationToken);
        if (!holdsOwner) return false;

        var activeOwners = await context.TenantMemberships.CountAsync(
            candidate => candidate.TenantId == platform.Id &&
                         candidate.Status == MembershipStatus.Active &&
                         context.MembershipRoles.Any(assignment =>
                             assignment.MembershipId == candidate.Id &&
                             context.TenantRoles.Any(role => role.Id == assignment.RoleId && role.NormalizedName == owner)),
            cancellationToken);

        return activeOwners <= 1;
    }
}

/// <summary>
/// Answers whether the caller proved the Platform second factor recently enough to change something
/// (IA-REQ-041/043).
/// <para>
/// The window is fifteen minutes. The SPEC says only "recent", so this is a choice: long enough that an
/// administrator working through a task is not re-prompted between two related changes, short enough that an
/// unattended session cannot be used to make one. It is one value in one place so that no handler can decide
/// differently.
/// </para>
/// </summary>
public sealed class RecentMfaVerifier(ApplicationDbContext context, ICurrentSession session, TimeProvider timeProvider) : IRecentMfaVerifier
{
    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public async Task<bool> HasRecentStepUpAsync(CancellationToken cancellationToken)
    {
        if (session.IsInvalid || session.IdentityId is not { } identityId || session.SessionId is not { } sessionId)
        {
            return false;
        }

        var enrollment = await context.PlatformMfaEnrollments
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.IdentityId == identityId, cancellationToken);

        return enrollment is not null &&
               enrollment.HasRecentStepUp(sessionId.Value, timeProvider.GetUtcNow(), Window);
    }
}
