using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Platform.Administrators;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Organizations;

/// <summary>
/// Suspends one Organization tenant, with the reason it is being suspended for (IA-REQ-043). The reason comes from
/// a closed set rather than free text, so a read-only security projection cannot become a place operators write
/// prose — very plausibly containing personal data — into.
/// </summary>
[Authorize(Permissions.PlatformTenantsManage, true)]
public sealed record SuspendOrganizationTenantCommand(Guid TenantId, TenantSuspensionReason Reason) : IRequest<Result>;

/// <summary>Returns a suspended Organization to service.</summary>
[Authorize(Permissions.PlatformTenantsManage, true)]
public sealed record ReactivateOrganizationTenantCommand(Guid TenantId) : IRequest<Result>;

public sealed class SuspendOrganizationTenantCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    ICurrentTenant currentTenant,
    IRecentMfaVerifier recentMfa,
    TimeProvider timeProvider) : IRequestHandler<SuspendOrganizationTenantCommand, Result>
{
    public Task<Result> Handle(SuspendOrganizationTenantCommand request, CancellationToken cancellationToken) =>
        OrganizationLifecycle.ApplyAsync(
            transaction, context, currentTenant, recentMfa, request.TenantId, cancellationToken,
            target =>
            {
                if (!Enum.IsDefined(request.Reason)) return null;
                if (target.Status != TenantStatus.Active) return null;
                target.Suspend(request.Reason, timeProvider.GetUtcNow());
                return ("platform.organization.suspended", request.Reason.ToString());
            });
}

public sealed class ReactivateOrganizationTenantCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    ICurrentTenant currentTenant,
    IRecentMfaVerifier recentMfa) : IRequestHandler<ReactivateOrganizationTenantCommand, Result>
{
    public Task<Result> Handle(ReactivateOrganizationTenantCommand request, CancellationToken cancellationToken) =>
        OrganizationLifecycle.ApplyAsync(
            transaction, context, currentTenant, recentMfa, request.TenantId, cancellationToken,
            target =>
            {
                if (target.Status != TenantStatus.Suspended) return null;
                target.Reactivate();
                return ("platform.organization.reactivated", "reactivated");
            });
}

/// <summary>
/// The half the two lifecycle operations share: prove recent MFA, resolve the acting Platform tenant, find the
/// target, refuse anything that is not an Organization, apply the transition, and audit it.
/// <para>
/// Platform is excluded here as well as in the aggregate. The aggregate's refusal is the one that matters, but a
/// route that reached it at all would still be a route that names Platform as a target, and IA-REQ-043 says these
/// endpoints must not.
/// </para>
/// <para>
/// The write is conditional on the row's concurrency token, so two administrators acting at once do not silently
/// overwrite each other: the first persists, and the second is told the tenant moved under it and can re-decide
/// against the state that won (IA-REQ-035).
/// </para>
/// </summary>
internal static class OrganizationLifecycle
{
    internal static async Task<Result> ApplyAsync(
        IApplicationTransaction transaction,
        IApplicationDbContext context,
        ICurrentTenant currentTenant,
        IRecentMfaVerifier recentMfa,
        Guid targetTenantId,
        CancellationToken cancellationToken,
        Func<Tenant, (string Code, string Outcome)?> apply)
    {
        if (!await recentMfa.HasRecentStepUpAsync(cancellationToken))
        {
            return Result.Failure(IdentityAccessErrors.RecentMfaRequired());
        }

        if (targetTenantId == Guid.Empty) return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());

        try
        {
            return await transaction.ExecuteAsync(async ct =>
            {
                var platform = await PlatformContext.ResolveAsync(context, currentTenant, ct);
                if (platform is null) return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());

                var id = TenantId.From(targetTenantId);
                var target = await context.Tenants.SingleOrDefaultAsync(candidate => candidate.Id == id, ct);
                if (target is null || target.Type != TenantType.Organization)
                {
                    return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());
                }

                var applied = apply(target);
                if (applied is not { } effect) return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());

                context.AuditEvents.Add(AuditEvent.Create(
                    target.Id,
                    null,
                    effect.Code,
                    $"platform-lifecycle-{target.Id.Value:N}",
                    new Dictionary<string, string> { ["code"] = effect.Code, ["outcome"] = effect.Outcome }));

                await context.SaveChangesAsync(ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(IdentityAccessErrors.PlatformTenantConcurrencyConflict());
        }
    }
}
