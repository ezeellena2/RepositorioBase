using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Application.IdentityAccess.Invitations;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Roles;

public sealed class GetPermissionCatalogQueryHandler(
    IRoleAdministrationStore roles,
    ICurrentTenant currentTenant,
    IUser user) : IRequestHandler<GetPermissionCatalogQuery, Result<IReadOnlyList<PermissionCatalogEntry>>>
{
    public async Task<Result<IReadOnlyList<PermissionCatalogEntry>>> Handle(GetPermissionCatalogQuery request, CancellationToken cancellationToken)
    {
        if (!RoleScope.Resolve(request.TenantId, currentTenant, user, out var tenantId, out var actorId))
            return Result<IReadOnlyList<PermissionCatalogEntry>>.Failure(IdentityAccessErrors.RoleNotFound());

        // Everything an Organization may hold, each entry saying whether this caller could grant it. Showing the
        // ceiling is what lets a screen offer only what will be accepted, instead of letting somebody compose a
        // role the server then refuses without being able to say which code was the problem.
        var grantable = (await roles.GrantableCodesAsync(tenantId, actorId, cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var entries = Permissions.Catalog
            .Where(definition => definition.AllowedTenantTypes.Contains(TenantType.Organization))
            .Select(definition => new PermissionCatalogEntry(definition.Code, grantable.Contains(definition.Code)))
            .OrderBy(entry => entry.Code, StringComparer.Ordinal)
            .ToArray();
        return Result<IReadOnlyList<PermissionCatalogEntry>>.Success(entries);
    }
}

public sealed class ListRolesQueryHandler(
    IRoleAdministrationStore roles,
    ICurrentTenant currentTenant,
    IUser user) : IRequestHandler<ListRolesQuery, Result<PaginatedList<RoleView>>>
{
    public async Task<Result<PaginatedList<RoleView>>> Handle(ListRolesQuery request, CancellationToken cancellationToken)
    {
        if (!RoleScope.Resolve(request.TenantId, currentTenant, user, out var tenantId, out _))
            return Result<PaginatedList<RoleView>>.Failure(IdentityAccessErrors.RoleNotFound());

        return Result<PaginatedList<RoleView>>.Success(await roles.ListAsync(tenantId, request.Pagination, cancellationToken));
    }
}

public sealed class GetRoleQueryHandler(
    IRoleAdministrationStore roles,
    ICurrentTenant currentTenant,
    IUser user) : IRequestHandler<GetRoleQuery, Result<RoleView>>
{
    public async Task<Result<RoleView>> Handle(GetRoleQuery request, CancellationToken cancellationToken)
    {
        if (!RoleScope.Resolve(request.TenantId, currentTenant, user, out var tenantId, out _))
            return Result<RoleView>.Failure(IdentityAccessErrors.RoleNotFound());

        // Another tenant's role is absent, not forbidden: `403` would confirm the identifier exists somewhere.
        var role = await roles.FindAsync(tenantId, request.RoleId, cancellationToken);
        return role is null
            ? Result<RoleView>.Failure(IdentityAccessErrors.RoleNotFound())
            : Result<RoleView>.Success(role);
    }
}

public sealed class CreateRoleCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IRoleAdministrationStore roles,
    IRecentIdentityProofStore proofs,
    ICurrentTenant currentTenant,
    ICurrentSession currentSession,
    IUser user) : IRequestHandler<CreateRoleCommand, Result<RoleView>>
{
    public async Task<Result<RoleView>> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        if (!RoleScope.Resolve(request.TenantId, currentTenant, user, out var tenantId, out var actorId))
            return Result<RoleView>.Failure(IdentityAccessErrors.InvalidRoleOperation());
        if (currentSession.SessionId is not { } sessionId)
            return Result<RoleView>.Failure(IdentityAccessErrors.InvalidSession());

        var requested = RoleAuthority.Normalize(request.PermissionCodes);
        return await RoleAuthority.RunAsync(transaction, async ct =>
        {
            if (!await proofs.TryConsumeAsync(actorId, sessionId, ProofActions.RoleChange, ct))
                return Result<RoleView>.Failure(IdentityAccessErrors.RecentProofRequired());
            if (!await RoleAuthority.IsActiveOrganizationAsync(context, tenantId, ct))
                return Result<RoleView>.Failure(IdentityAccessErrors.InvalidRoleOperation());

            // Every code is an addition, so the ceiling is applied to the whole set.
            var ceiling = await roles.GrantableCodesAsync(tenantId, actorId, ct);
            if (!RoleAuthority.WithinCeiling(requested, ceiling))
                return Result<RoleView>.Failure(IdentityAccessErrors.InvalidRoleOperation());

            var written = await roles.CreateAsync(tenantId, new RoleEdit(request.Name ?? string.Empty, requested), ct);
            return written.Status switch
            {
                RoleWriteStatus.Applied => Result<RoleView>.Success(written.Role!),
                RoleWriteStatus.VersionConflict => Result<RoleView>.Failure(IdentityAccessErrors.RoleConcurrencyConflict()),
                _ => Result<RoleView>.Failure(IdentityAccessErrors.InvalidRoleOperation())
            };
        }, cancellationToken);
    }
}

public sealed class UpdateRoleCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IRoleAdministrationStore roles,
    IRecentIdentityProofStore proofs,
    IRoleAuthorityLock authorityLock,
    ICurrentTenant currentTenant,
    ICurrentSession currentSession,
    IUser user,
    TimeProvider timeProvider) : IRequestHandler<UpdateRoleCommand, Result<RoleView>>
{
    public async Task<Result<RoleView>> Handle(UpdateRoleCommand request, CancellationToken cancellationToken)
    {
        if (!RoleScope.Resolve(request.TenantId, currentTenant, user, out var tenantId, out var actorId))
            return Result<RoleView>.Failure(IdentityAccessErrors.InvalidRoleOperation());
        if (currentSession.SessionId is not { } sessionId)
            return Result<RoleView>.Failure(IdentityAccessErrors.InvalidSession());
        if (string.IsNullOrWhiteSpace(request.Version))
            return Result<RoleView>.Failure(IdentityAccessErrors.InvalidRoleOperation());

        var requested = RoleAuthority.Normalize(request.PermissionCodes);
        return await RoleAuthority.RunAsync(transaction, async ct =>
        {
            // Taken before anything is read or written, so this transaction holds nothing while it waits. An
            // offer being established right now cannot commit past it, and one that has not started cannot begin
            // until this commits and its rows become cancellable (IA-REQ-047).
            await authorityLock.AcquireAsync(tenantId, ct);

            if (!await proofs.TryConsumeAsync(actorId, sessionId, ProofActions.RoleChange, ct))
                return Result<RoleView>.Failure(IdentityAccessErrors.RecentProofRequired());
            if (!await RoleAuthority.IsActiveOrganizationAsync(context, tenantId, ct))
                return Result<RoleView>.Failure(IdentityAccessErrors.InvalidRoleOperation());

            var held = await roles.HeldCodesAsync(tenantId, request.RoleId, ct);
            if (held is null) return Result<RoleView>.Failure(IdentityAccessErrors.RoleNotFound());

            // The ceiling is evaluated on the added codes only. Removing something the actor does not itself hold
            // is a narrowing, and refusing that would let one administrator's role permanently outrank another's.
            var added = requested.Except(held, StringComparer.Ordinal).ToArray();
            var ceiling = await roles.GrantableCodesAsync(tenantId, actorId, ct);
            if (!RoleAuthority.WithinCeiling(added, ceiling))
                return Result<RoleView>.Failure(IdentityAccessErrors.InvalidRoleOperation());

            // Cancelled before the write and flushed with it, so a token already in somebody's mailbox cannot be
            // accepted into authority nobody offered them. Tracked here, committed by the store's save.
            if (added.Length > 0)
                await RoleAuthority.CancelOffersAsync(context, tenantId, request.RoleId, "role-widened", actorId, timeProvider.GetUtcNow(), ct);

            var written = await roles.UpdateAsync(tenantId, request.RoleId, new RoleEdit(request.Name ?? string.Empty, requested), request.Version, ct);
            if (written.Status != RoleWriteStatus.Applied) return RoleAuthority.Failure<RoleView>(written.Status);

            // Counted from flushed state, and refused by throwing: a floor checked after the write can only be
            // honoured by rolling the write back.
            RoleAuthority.EnsureFloor(await roles.CountAdministratorsAsync(tenantId, ct));
            return Result<RoleView>.Success(written.Role!);
        }, cancellationToken);
    }
}

public sealed class RetireRoleCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IRoleAdministrationStore roles,
    IRecentIdentityProofStore proofs,
    IRoleAuthorityLock authorityLock,
    ICurrentTenant currentTenant,
    ICurrentSession currentSession,
    IUser user,
    TimeProvider timeProvider) : IRequestHandler<RetireRoleCommand, Result>
{
    public async Task<Result> Handle(RetireRoleCommand request, CancellationToken cancellationToken)
    {
        if (!RoleScope.Resolve(request.TenantId, currentTenant, user, out var tenantId, out var actorId))
            return Result.Failure(IdentityAccessErrors.InvalidRoleOperation());
        if (currentSession.SessionId is not { } sessionId)
            return Result.Failure(IdentityAccessErrors.InvalidSession());
        if (string.IsNullOrWhiteSpace(request.Version))
            return Result.Failure(IdentityAccessErrors.InvalidRoleOperation());

        return await RoleAuthority.RunAsync(transaction, async ct =>
        {
            // Taken before anything is read or written, so this transaction holds nothing while it waits. An
            // offer being established right now cannot commit past it, and one that has not started cannot begin
            // until this commits and its rows become cancellable (IA-REQ-047).
            await authorityLock.AcquireAsync(tenantId, ct);

            if (!await proofs.TryConsumeAsync(actorId, sessionId, ProofActions.RoleChange, ct))
                return Result.Failure(IdentityAccessErrors.RecentProofRequired());
            if (!await RoleAuthority.IsActiveOrganizationAsync(context, tenantId, ct))
                return Result.Failure(IdentityAccessErrors.InvalidRoleOperation());

            await RoleAuthority.CancelOffersAsync(context, tenantId, request.RoleId, "role-retired", actorId, timeProvider.GetUtcNow(), ct);

            var written = await roles.RetireAsync(tenantId, request.RoleId, request.Version, ct);

            // Retiring what is already retired, with a current version, is the state the caller asked for.
            if (written.Status == RoleWriteStatus.AlreadyInState) return Result.Success();
            if (written.Status != RoleWriteStatus.Applied) return RoleAuthority.Failure(written.Status);

            RoleAuthority.EnsureFloor(await roles.CountAdministratorsAsync(tenantId, ct));
            return Result.Success();
        }, cancellationToken);
    }
}

/// <summary>
/// What every role request agrees on before it does anything: that the tenant it names is the one the session is
/// operating in, and that the caller is an identity.
/// </summary>
internal static class RoleScope
{
    internal static bool Resolve(TenantId named, ICurrentTenant currentTenant, IUser user, out TenantId tenantId, out Guid actorId)
    {
        tenantId = default;
        actorId = default;

        // The route may name any tenant; only the session decides which one the caller is in. The pipeline proved
        // the permission against the session's tenant, so honouring another would authorize A and act on B.
        if (named.IsEmpty || currentTenant.TenantId is not { } active || active != named) return false;
        if (user.Id is not { } id || id == Guid.Empty) return false;

        tenantId = active;
        actorId = id;
        return true;
    }
}

/// <summary>
/// The rules every role write shares, in one place because a handler that quietly skipped one would still compile
/// and still pass its own happy path.
/// </summary>
internal static class RoleAuthority
{
    internal static IReadOnlyList<string> Normalize(IReadOnlyList<string>? requested) =>
        (requested ?? []).Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    internal static bool WithinCeiling(IReadOnlyList<string> requested, IReadOnlyList<string> ceiling) =>
        requested.All(code => ceiling.Contains(code, StringComparer.Ordinal));

    /// <summary>
    /// `Personal` and `Platform` are refused here rather than by the permission, because both may legitimately
    /// hold `roles.manage` and neither has delegated administration to do (IA-REQ-053).
    /// </summary>
    internal static async Task<bool> IsActiveOrganizationAsync(IApplicationDbContext context, TenantId tenantId, CancellationToken cancellationToken) =>
        await context.Tenants.AnyAsync(
            tenant => tenant.Id == tenantId && tenant.Type == TenantType.Organization && tenant.Status == TenantStatus.Active,
            cancellationToken);

    /// <summary>
    /// Withdraws every pending offer naming this role and retires its still-undelivered envelope, so a widened or
    /// retired role cannot reach acceptance through a token somebody is already holding (IA-REQ-047).
    /// </summary>
    internal static async Task CancelOffersAsync(
        IApplicationDbContext context,
        TenantId tenantId,
        Guid roleId,
        string reason,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var role = RoleId.From(roleId);
        var offered = await context.InvitationRoles
            .Where(link => link.TenantId == tenantId && link.RoleId == role)
            .Select(link => link.InvitationId)
            .ToListAsync(cancellationToken);
        if (offered.Count == 0) return;

        var invitations = await context.Invitations
            .Where(invitation => invitation.TenantId == tenantId && offered.Contains(invitation.Id) && invitation.Status == InvitationStatus.Pending)
            .ToListAsync(cancellationToken);
        if (invitations.Count == 0) return;

        var settled = InvitationDelivery.ToStorablePrecision(now);
        var tenant = await context.Tenants.SingleAsync(candidate => candidate.Id == tenantId, cancellationToken);
        foreach (var invitation in invitations)
        {
            invitation.Cancel(tenant, settled);
            await InvitationDelivery.RetireAsync(context, invitation.TokenHash, reason, settled, cancellationToken);
            context.AuditEvents.Add(AuditEvent.Create(
                tenantId,
                actorId,
                "invitation.cancelled",
                $"invitation-{invitation.Id.Value:N}",
                new Dictionary<string, string> { ["code"] = "invitation.cancelled", ["outcome"] = reason }));
        }
    }

    internal static void EnsureFloor(int administrators)
    {
        if (administrators == 0) throw new AdministratorFloorViolation();
    }

    internal static Result<T> Failure<T>(RoleWriteStatus status) => Result<T>.Failure(Describe(status));

    internal static Result Failure(RoleWriteStatus status) => Result.Failure(Describe(status));

    /// <summary>
    /// Runs the body inside the ambient transaction and turns the two refusals that can only be discovered after
    /// a write into their contract answers. Both have to leave the database as they found it, and with this
    /// transaction the only way to do that is to let the exception out, so both arrive here as exceptions.
    /// </summary>
    internal static async Task<Result<T>> RunAsync<T>(
        IApplicationTransaction transaction,
        Func<CancellationToken, Task<Result<T>>> body,
        CancellationToken cancellationToken)
    {
        try { return await transaction.ExecuteAsync(body, cancellationToken); }
        catch (AdministratorFloorViolation) { return Result<T>.Failure(IdentityAccessErrors.LastAdministratorRequired()); }
        // The ceiling is declared in this class and raised, today, only from the membership side. Translating it
        // here is what makes the declaration honest: an exception a module owns but does not answer for is a 500
        // waiting for the first role write that re-reads its ceiling after committing.
        catch (GrantCeilingViolation) { return Result<T>.Failure(IdentityAccessErrors.InvalidRoleOperation()); }
        catch (DbUpdateConcurrencyException) { return Result<T>.Failure(IdentityAccessErrors.RoleConcurrencyConflict()); }
    }

    internal static async Task<Result> RunAsync(
        IApplicationTransaction transaction,
        Func<CancellationToken, Task<Result>> body,
        CancellationToken cancellationToken)
    {
        try { return await transaction.ExecuteAsync(body, cancellationToken); }
        catch (AdministratorFloorViolation) { return Result.Failure(IdentityAccessErrors.LastAdministratorRequired()); }
        catch (GrantCeilingViolation) { return Result.Failure(IdentityAccessErrors.InvalidRoleOperation()); }
        catch (DbUpdateConcurrencyException) { return Result.Failure(IdentityAccessErrors.RoleConcurrencyConflict()); }
    }

    private static ApplicationError Describe(RoleWriteStatus status) => status switch
    {
        RoleWriteStatus.NotFound => IdentityAccessErrors.RoleNotFound(),
        RoleWriteStatus.VersionConflict => IdentityAccessErrors.RoleConcurrencyConflict(),
        _ => IdentityAccessErrors.InvalidRoleOperation()
    };

    /// <summary>
    /// The floor refusal, as an exception because it is discovered after the write it must undo. It never reaches
    /// a caller as an exception: the handler turns it into `last_administrator_required`.
    /// </summary>
    internal sealed class AdministratorFloorViolation : Exception;

    /// <summary>
    /// The ceiling refusal, for the same reason and in the same shape: it is discovered after the write, so the
    /// only way to honour it is to roll that write back. The handler turns it into the same answer the caller
    /// would have received had the authority change landed a moment earlier.
    /// </summary>
    internal sealed class GrantCeilingViolation : Exception;
}
