using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Context.GetIdentityContext;

public sealed class GetIdentityContextQueryHandler(IApplicationDbContext context, ICurrentSession currentSession, IIdentityAccountService identities) : IRequestHandler<GetIdentityContextQuery, Result<IdentityContext>>
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
        return Result<IdentityContext>.Success(new IdentityContext(identity.Id, identity.Email, identity.IsActive, activeTenant, tenants, [], session.AbsoluteExpiresAt, false));
    }
}
