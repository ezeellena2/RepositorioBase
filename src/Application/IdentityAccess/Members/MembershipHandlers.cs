using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Application.IdentityAccess.Roles;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Members;

public sealed class ListMembersQueryHandler(
    IMembershipAdministrationStore members,
    ICurrentTenant currentTenant,
    IUser user) : IRequestHandler<ListMembersQuery, Result<MemberPage>>
{
    public async Task<Result<MemberPage>> Handle(ListMembersQuery request, CancellationToken cancellationToken)
    {
        if (!RoleScope.Resolve(request.TenantId, currentTenant, user, out var tenantId, out _))
            return Result<MemberPage>.Failure(IdentityAccessErrors.InvalidMembershipOperation());

        return Result<MemberPage>.Success(await members.ListAsync(tenantId, request.Limit, request.Cursor, cancellationToken));
    }
}

public sealed class ListTenantInvitationsQueryHandler(
    IMembershipAdministrationStore members,
    ICurrentTenant currentTenant,
    IUser user) : IRequestHandler<ListTenantInvitationsQuery, Result<InvitationSummaryPage>>
{
    public async Task<Result<InvitationSummaryPage>> Handle(ListTenantInvitationsQuery request, CancellationToken cancellationToken)
    {
        if (!RoleScope.Resolve(request.TenantId, currentTenant, user, out var tenantId, out _))
            return Result<InvitationSummaryPage>.Failure(IdentityAccessErrors.InvalidMembershipOperation());

        return Result<InvitationSummaryPage>.Success(await members.ListInvitationsAsync(tenantId, request.Limit, request.Cursor, cancellationToken));
    }
}

public sealed class UpdateMemberRolesCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IMembershipAdministrationStore members,
    IRoleAdministrationStore roles,
    IRecentIdentityProofStore proofs,
    ICurrentTenant currentTenant,
    ICurrentSession currentSession,
    IUser user) : IRequestHandler<UpdateMemberRolesCommand, Result<MemberView>>
{
    public async Task<Result<MemberView>> Handle(UpdateMemberRolesCommand request, CancellationToken cancellationToken)
    {
        if (!RoleScope.Resolve(request.TenantId, currentTenant, user, out var tenantId, out var actorId))
            return Result<MemberView>.Failure(IdentityAccessErrors.InvalidMembershipOperation());
        if (currentSession.SessionId is not { } sessionId)
            return Result<MemberView>.Failure(IdentityAccessErrors.InvalidSession());
        if (string.IsNullOrWhiteSpace(request.Version))
            return Result<MemberView>.Failure(IdentityAccessErrors.InvalidMembershipOperation());

        var requested = (request.RoleIds ?? []).Distinct().ToArray();
        return await MembershipAuthority.RunAsync(transaction, async ct =>
        {
            if (!await proofs.TryConsumeAsync(actorId, sessionId, ProofActions.MemberRoleChange, ct))
                return Result<MemberView>.Failure(IdentityAccessErrors.RecentProofRequired());
            if (!await MembershipAuthority.IsActiveOrganizationAsync(context, tenantId, ct))
                return Result<MemberView>.Failure(IdentityAccessErrors.InvalidMembershipOperation());

            // The whole set, not the difference. Assigning a role hands over everything in it, so an actor who
            // could not have built that role must not be able to hand it to somebody either.
            var conferred = await members.CodesOfRolesAsync(tenantId, requested, ct);
            if (conferred is null) return Result<MemberView>.Failure(IdentityAccessErrors.InvalidMembershipOperation());

            var ceiling = await roles.GrantableCodesAsync(tenantId, actorId, ct);
            if (!conferred.All(code => ceiling.Contains(code, StringComparer.Ordinal)))
                return Result<MemberView>.Failure(IdentityAccessErrors.InvalidMembershipOperation());

            var written = await members.ReplaceRolesAsync(tenantId, request.MembershipId, requested, request.Version, ct);
            if (written.Status != MembershipWriteStatus.Applied) return MembershipAuthority.Failure<MemberView>(written.Status);

            context.AuditEvents.Add(MembershipAuthority.Audited(tenantId, actorId, request.MembershipId, "changed"));
            await context.SaveChangesAsync(ct);

            MembershipAuthority.EnsureFloor(await roles.CountAdministratorsAsync(tenantId, ct));
            return Result<MemberView>.Success(written.Member!);
        }, cancellationToken);
    }
}

public sealed class ChangeMemberStatusCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IMembershipAdministrationStore members,
    IRoleAdministrationStore roles,
    ICurrentTenant currentTenant,
    IUser user) : IRequestHandler<ChangeMemberStatusCommand, Result>
{
    public async Task<Result> Handle(ChangeMemberStatusCommand request, CancellationToken cancellationToken)
    {
        if (!RoleScope.Resolve(request.TenantId, currentTenant, user, out var tenantId, out var actorId))
            return Result.Failure(IdentityAccessErrors.InvalidMembershipOperation());
        if (string.IsNullOrWhiteSpace(request.Version))
            return Result.Failure(IdentityAccessErrors.InvalidMembershipOperation());

        var target = request.Change switch
        {
            MemberStatusChange.Suspend => MembershipStatus.Suspended,
            MemberStatusChange.Reactivate => MembershipStatus.Active,
            _ => MembershipStatus.Revoked
        };

        return await MembershipAuthority.RunAsync(transaction, async ct =>
        {
            if (!await MembershipAuthority.IsActiveOrganizationAsync(context, tenantId, ct))
                return Result.Failure(IdentityAccessErrors.InvalidMembershipOperation());

            // The owner's own membership cannot be suspended or revoked. An organization whose owner is not a
            // member of it has nobody who can transfer it, so the way out is to transfer first (IA-REQ-053).
            if (target != MembershipStatus.Active && await members.OwnerAsync(tenantId, ct) == request.MembershipId)
                return Result.Failure(IdentityAccessErrors.InvalidMembershipOperation());

            var written = await members.SetStatusAsync(tenantId, request.MembershipId, target, request.Version, ct);
            if (written.Status == MembershipWriteStatus.AlreadyInState) return Result.Success();
            if (written.Status != MembershipWriteStatus.Applied) return MembershipAuthority.Failure(written.Status);

            context.AuditEvents.Add(MembershipAuthority.Audited(tenantId, actorId, request.MembershipId, request.Change switch
            {
                MemberStatusChange.Suspend => "suspended",
                MemberStatusChange.Reactivate => "reactivated",
                _ => "revoked"
            }));
            await context.SaveChangesAsync(ct);

            MembershipAuthority.EnsureFloor(await roles.CountAdministratorsAsync(tenantId, ct));
            return Result.Success();
        }, cancellationToken);
    }
}

public sealed class TransferOwnershipCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IMembershipAdministrationStore members,
    IRoleAdministrationStore roles,
    IRecentIdentityProofStore proofs,
    ICurrentTenant currentTenant,
    ICurrentSession currentSession,
    IUser user,
    TimeProvider timeProvider) : IRequestHandler<TransferOwnershipCommand, Result>
{
    internal const string MessageType = "identity.ownership.transferred.notice.requested";

    public async Task<Result> Handle(TransferOwnershipCommand request, CancellationToken cancellationToken)
    {
        if (!RoleScope.Resolve(request.TenantId, currentTenant, user, out var tenantId, out var actorId))
            return Result.Failure(IdentityAccessErrors.InvalidMembershipOperation());
        if (currentSession.SessionId is not { } sessionId)
            return Result.Failure(IdentityAccessErrors.InvalidSession());
        if (string.IsNullOrWhiteSpace(request.Version))
            return Result.Failure(IdentityAccessErrors.InvalidMembershipOperation());

        return await MembershipAuthority.RunAsync(transaction, async ct =>
        {
            if (!await MembershipAuthority.IsActiveOrganizationAsync(context, tenantId, ct))
                return Result.Failure(IdentityAccessErrors.InvalidMembershipOperation());

            // Holding the permission is necessary and never sufficient: it has to be your own organization to
            // give away. This is checked before the proof is spent, because being refused for who you are should
            // not cost a proof.
            var owner = await members.OwnerAsync(tenantId, ct);
            var mine = await context.TenantMemberships
                .Where(membership => membership.TenantId == tenantId && membership.IdentityId == actorId)
                .Select(membership => (Guid?)membership.Id.Value)
                .SingleOrDefaultAsync(ct);
            if (owner is null || mine is null || owner != mine) return Result.Failure(IdentityAccessErrors.OwnerRequired());

            if (!await proofs.TryConsumeAsync(actorId, sessionId, ProofActions.OwnershipTransfer, ct))
                return Result.Failure(IdentityAccessErrors.RecentProofRequired());

            var written = await members.TransferOwnershipAsync(tenantId, request.ToMembershipId, request.Version, ct);
            if (written.Status == MembershipWriteStatus.AlreadyInState) return Result.Success();
            if (written.Status != MembershipWriteStatus.Applied) return MembershipAuthority.Failure(written.Status);

            var now = timeProvider.GetUtcNow();
            context.AuditEvents.Add(MembershipAuthority.Audited(tenantId, actorId, request.ToMembershipId, "ownership-transferred"));

            // Both people are told. The payload carries the two membership identifiers and the tenant and nothing
            // else — no address, no token, and so no `OutboxSecret` to seal or retire.
            context.OutboxMessages.Add(OutboxMessage.Create(
                MessageType,
                JsonSerializer.Serialize(new OwnershipTransferNotice(tenantId.Value, owner.Value, request.ToMembershipId)),
                now));
            await context.SaveChangesAsync(ct);

            MembershipAuthority.EnsureFloor(await roles.CountAdministratorsAsync(tenantId, ct));
            return Result.Success();
        }, cancellationToken);
    }

    private sealed record OwnershipTransferNotice(Guid TenantId, Guid FromMembershipId, Guid ToMembershipId);
}

/// <summary>
/// The rules every membership write shares. They live together for the same reason the role ones do: a handler
/// that quietly skipped one would still compile and still pass its own happy path.
/// </summary>
internal static class MembershipAuthority
{
    internal static async Task<bool> IsActiveOrganizationAsync(IApplicationDbContext context, TenantId tenantId, CancellationToken cancellationToken) =>
        await context.Tenants.AnyAsync(
            tenant => tenant.Id == tenantId && tenant.Type == TenantType.Organization && tenant.Status == TenantStatus.Active,
            cancellationToken);

    /// <summary>
    /// The actor-attributed record. The interceptor writes its own `membership.changed` row with a null actor for
    /// every changed association; this is the one that says who did it and why (IA-REQ-026).
    /// </summary>
    internal static AuditEvent Audited(TenantId tenantId, Guid actorId, Guid membershipId, string outcome) =>
        AuditEvent.Create(
            tenantId,
            actorId,
            "membership.changed",
            $"membership-{membershipId:N}",
            new Dictionary<string, string> { ["code"] = "membership.changed", ["outcome"] = outcome });

    internal static void EnsureFloor(int administrators)
    {
        if (administrators == 0) throw new RoleAuthority.AdministratorFloorViolation();
    }

    internal static Result<T> Failure<T>(MembershipWriteStatus status) => Result<T>.Failure(Describe(status));

    internal static Result Failure(MembershipWriteStatus status) => Result.Failure(Describe(status));

    internal static async Task<Result<T>> RunAsync<T>(
        IApplicationTransaction transaction,
        Func<CancellationToken, Task<Result<T>>> body,
        CancellationToken cancellationToken)
    {
        try { return await transaction.ExecuteAsync(body, cancellationToken); }
        catch (RoleAuthority.AdministratorFloorViolation) { return Result<T>.Failure(IdentityAccessErrors.LastAdministratorRequired()); }
        catch (DbUpdateConcurrencyException) { return Result<T>.Failure(IdentityAccessErrors.MembershipConcurrencyConflict()); }
    }

    internal static async Task<Result> RunAsync(
        IApplicationTransaction transaction,
        Func<CancellationToken, Task<Result>> body,
        CancellationToken cancellationToken)
    {
        try { return await transaction.ExecuteAsync(body, cancellationToken); }
        catch (RoleAuthority.AdministratorFloorViolation) { return Result.Failure(IdentityAccessErrors.LastAdministratorRequired()); }
        catch (DbUpdateConcurrencyException) { return Result.Failure(IdentityAccessErrors.MembershipConcurrencyConflict()); }
    }

    private static ApplicationError Describe(MembershipWriteStatus status) => status switch
    {
        MembershipWriteStatus.NotFound => IdentityAccessErrors.MembershipNotFound(),
        MembershipWriteStatus.VersionConflict => IdentityAccessErrors.MembershipConcurrencyConflict(),
        _ => IdentityAccessErrors.InvalidMembershipOperation()
    };
}
