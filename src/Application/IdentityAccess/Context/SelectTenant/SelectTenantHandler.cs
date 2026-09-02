using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Context.GetIdentityContext;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Context.SelectTenant;

public sealed class SelectTenantCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    ICurrentSession currentSession,
    IIdentityAccountService identities,
    TimeProvider timeProvider) : IRequestHandler<SelectTenantCommand, Result<IdentityContext>>
{
    public Task<Result<IdentityContext>> Handle(SelectTenantCommand request, CancellationToken cancellationToken)
    {
        if (request.TenantId.IsEmpty)
            return Task.FromResult(Result<IdentityContext>.Failure(new ApplicationError("invalid_request", ApplicationErrorCategory.Validation)));
        if (currentSession.IsInvalid || currentSession.SessionId is null || currentSession.IdentityId is null)
            return Task.FromResult(Result<IdentityContext>.Failure(IdentityAccessErrors.InvalidSession()));

        return transaction.ExecuteAsync(async ct =>
        {
            var session = await context.UserSessions.SingleOrDefaultAsync(candidate => candidate.Id == currentSession.SessionId.Value && candidate.IdentityId == currentSession.IdentityId.Value, ct);
            var account = await identities.FindByIdAsync(currentSession.IdentityId.Value, ct);
            var selectedEntity = await (from membership in context.TenantMemberships
                                        join tenant in context.Tenants on membership.TenantId equals tenant.Id
                                        where membership.IdentityId == currentSession.IdentityId.Value && membership.TenantId == request.TenantId && membership.Status == MembershipStatus.Active && tenant.Status == TenantStatus.Active
                                        select tenant).SingleOrDefaultAsync(ct);
            if (session is null || account is null || !account.IsActive || !session.IsActiveAt(timeProvider.GetUtcNow()))
                return Result<IdentityContext>.Failure(IdentityAccessErrors.InvalidSession());
            if (selectedEntity is null)
                return Result<IdentityContext>.Failure(new ApplicationError("permission_denied", ApplicationErrorCategory.Authorization));

            var now = timeProvider.GetUtcNow();
            session.SelectTenant(request.TenantId, now);
            var tenantEntities = await (from membership in context.TenantMemberships
                                        join tenant in context.Tenants on membership.TenantId equals tenant.Id
                                        where membership.IdentityId == account.Id && membership.Status == MembershipStatus.Active && tenant.Status == TenantStatus.Active
                                        orderby tenant.Id
                                        select tenant).ToListAsync(ct);
            var tenants = tenantEntities.Select(tenant => new TenantContext(tenant.Id.Value, tenant.Type.ToString(), tenant.Slug.Value)).ToArray();
            await context.SaveChangesAsync(ct);
            var selected = new TenantContext(selectedEntity.Id.Value, selectedEntity.Type.ToString(), selectedEntity.Slug.Value);
            return Result<IdentityContext>.Success(new IdentityContext(account.Id, account.Email, account.IsActive, selected, tenants, [], session.AbsoluteExpiresAt, false));
        }, cancellationToken);
    }
}
