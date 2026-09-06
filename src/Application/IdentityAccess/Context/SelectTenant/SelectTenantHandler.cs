using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Context.GetIdentityContext;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Platform;
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
    IEffectivePermissionReader permissions,
    IPlatformMfaSessionProof platformMfa,
    CleanArchitecture.Application.IdentityAccess.People.IPersonalDataMode personalDataMode,
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
            if (session is null || account is null || !account.IsActive)
                return Result<IdentityContext>.Failure(IdentityAccessErrors.InvalidSession());

            for (var attempt = 0; attempt < SessionWriteRetry.Attempts; attempt++)
            {
                var now = timeProvider.GetUtcNow();
                if (!session.IsActiveAt(now))
                    return Result<IdentityContext>.Failure(IdentityAccessErrors.InvalidSession());

                // Re-read on every attempt: whatever won the previous round may also have suspended the
                // membership or the tenant, and an authorization decision must never use pre-conflict state.
                var selectedEntity = await (from membership in context.TenantMemberships.AsNoTracking()
                                            join tenant in context.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
                                            where membership.IdentityId == currentSession.IdentityId.Value && membership.TenantId == request.TenantId && membership.Status == MembershipStatus.Active && tenant.Status == TenantStatus.Active
                                            select tenant).SingleOrDefaultAsync(ct);
                if (selectedEntity is null)
                    return Result<IdentityContext>.Failure(new ApplicationError("permission_denied", ApplicationErrorCategory.Authorization));

                session.SelectTenant(request.TenantId, now);
                try
                {
                    await context.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    await context.ReloadAsync(session, ct);
                    continue;
                }

                var tenantEntities = await (from membership in context.TenantMemberships.AsNoTracking()
                                            join tenant in context.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
                                            where membership.IdentityId == account.Id && membership.Status == MembershipStatus.Active && tenant.Status == TenantStatus.Active
                                            orderby tenant.Id
                                            select tenant).ToListAsync(ct);
                var tenants = tenantEntities.Select(tenant => new TenantContext(tenant.Id.Value, tenant.Type.ToString(), tenant.Slug.Value)).ToArray();
                var selected = new TenantContext(selectedEntity.Id.Value, selectedEntity.Type.ToString(), selectedEntity.Slug.Value);
                var effectivePermissions = await permissions.GetEffectivePermissionsAsync(account.Id, request.TenantId, ct);

                // Selecting Platform is not the same as being able to operate it: the directories require that
                // this session proved the second factor, so the answer says whether it still has to (IA-REQ-045).
                var requiresTwoFactor = selectedEntity.Type == TenantType.Platform &&
                                        !await platformMfa.HasProvedFactorAsync(ct);
                var displayName = await context.PersonProfiles
                    .AsNoTracking()
                    .Where(profile => profile.IdentityId == account.Id)
                    .Select(profile => profile.DisplayName)
                    .SingleOrDefaultAsync(ct) ?? account.Email;

                return Result<IdentityContext>.Success(new IdentityContext(
                    account.Id,
                    displayName,
                    account.IsActive,
                    selected,
                    tenants,
                    effectivePermissions,
                    session.AbsoluteExpiresAt,
                    requiresTwoFactor,
                    personalDataMode.Classification.ToString()));
            }

            return Result<IdentityContext>.Failure(IdentityAccessErrors.SessionConcurrencyConflict());
        }, cancellationToken);
    }
}
