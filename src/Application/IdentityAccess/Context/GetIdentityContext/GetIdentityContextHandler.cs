using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Platform;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Context.GetIdentityContext;

public sealed class GetIdentityContextQueryHandler(
    IApplicationDbContext context,
    ICurrentSession currentSession,
    IIdentityAccountService identities,
    IEffectivePermissionReader permissions,
    IPlatformMfaSessionProof platformMfa,
    CleanArchitecture.Application.IdentityAccess.People.IPersonalDataMode personalDataMode) : IRequestHandler<GetIdentityContextQuery, Result<IdentityContext>>
{
    public async Task<Result<IdentityContext>> Handle(GetIdentityContextQuery request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.SessionId is null || currentSession.IdentityId is null)
            return Result<IdentityContext>.Failure(IdentityAccessErrors.InvalidSession());

        var session = await context.UserSessions.SingleOrDefaultAsync(candidate => candidate.Id == currentSession.SessionId.Value && candidate.IdentityId == currentSession.IdentityId.Value, cancellationToken);
        var identity = await identities.FindByIdAsync(currentSession.IdentityId.Value, cancellationToken);
        if (session is null || identity is null || !identity.IsActive) return Result<IdentityContext>.Failure(IdentityAccessErrors.InvalidSession());
        var tenantEntities = await (from membership in context.TenantMemberships
                                    join tenant in context.Tenants on membership.TenantId equals tenant.Id
                                    where membership.IdentityId == identity.Id && membership.Status == MembershipStatus.Active && tenant.Status == TenantStatus.Active
                                    orderby tenant.Id
                                    select tenant).ToListAsync(cancellationToken);
        var tenants = tenantEntities.Select(tenant => new TenantContext(tenant.Id.Value, tenant.Type.ToString(), tenant.Slug.Value)).ToArray();
        var activeTenant = session.ActiveTenantId is { } selected
            ? tenants.SingleOrDefault(tenant => tenant.Id == selected.Value)
            : null;

        // Permissions belong to exactly one membership. Without a validated active tenant there is no membership
        // to project, so the client receives none rather than an accumulation across its tenants (IA-REQ-007).
        IReadOnlyList<string> effectivePermissions = activeTenant is null
            ? []
            : await permissions.GetEffectivePermissionsAsync(identity.Id, TenantId.From(activeTenant.Id), cancellationToken);

        // What the client needs in order to offer the step-up instead of a screen of refusals: operating inside
        // Platform requires that this session proved the second factor, and a password sign-in has not
        // (IA-REQ-045). It is asked only for Platform, because it is the only tenant type that requires it.
        var requiresTwoFactor = activeTenant is { Type: nameof(TenantType.Platform) } &&
                                !await platformMfa.HasProvedFactorAsync(cancellationToken);
        // A person who told us their name is called by it. The email stays the identifier, but it is not a name,
        // and showing it where a name belongs is how an address ends up on a screen somebody else can see
        // (IA-REQ-050 amends this section).
        var displayName = await context.PersonProfiles
            .AsNoTracking()
            .Where(profile => profile.IdentityId == identity.Id)
            .Select(profile => profile.DisplayName)
            .SingleOrDefaultAsync(cancellationToken) ?? identity.Email;

        return Result<IdentityContext>.Success(new IdentityContext(
            identity.Id,
            displayName,
            identity.IsActive,
            identity.PreferredLanguage,
            activeTenant,
            tenants,
            effectivePermissions,
            session.AbsoluteExpiresAt,
            requiresTwoFactor,
            personalDataMode.Classification.ToString()));
    }
}
