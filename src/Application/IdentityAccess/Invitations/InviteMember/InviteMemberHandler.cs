using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Invitations.InviteMember;

/// <summary>
/// Offers one identity a place in one organization. The offer, its rotation and its delivery intent are one
/// transaction (IA-REQ-017/027), and the usable token exists only long enough to be hashed into the aggregate and
/// sealed into an <see cref="OutboxSecret"/> — it is never returned, logged or written to a payload
/// (IA-REQ-015/018/029).
/// </summary>
public sealed class InviteMemberCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    ICurrentTenant currentTenant,
    IUser user,
    IIdentityAccountService identities,
    IOfferableRoleReader offerableRoles,
    IRoleAuthorityLock authorityLock,
    IRequestLanguage requestLanguage,
    ISecureTokenGenerator tokens,
    ITokenHasher tokenHasher,
    IOutboxSecretWriter secretWriter,
    TimeProvider timeProvider) : IRequestHandler<InviteMemberCommand, Result<IssuedInvitation>>
{
    internal const string MessageType = "identity.invitation.requested";
    private static readonly TimeSpan Window = TimeSpan.FromDays(7);

    public async Task<Result<IssuedInvitation>> Handle(InviteMemberCommand request, CancellationToken cancellationToken)
    {
        // The route may name any tenant; only the session decides which one the caller is operating in. The
        // pipeline has already proved members.invite against the session's tenant, so honouring a different one
        // here would be authorizing against A and acting on B.
        if (request.TenantId.IsEmpty || currentTenant.TenantId is not { } activeTenantId || activeTenantId != request.TenantId)
        {
            return Invalid();
        }

        if (user.Id is not { } inviterId || inviterId == Guid.Empty)
        {
            return Invalid();
        }

        var recipient = Invitation.Canonicalize(request.Email!);
        var requestedRoleIds = request.RoleIds!.Distinct().ToArray();

        // IA-REQ-005. The pipeline proves the permission, never the confirmation, so this gate is the handler's.
        if (await identities.FindByIdAsync(inviterId, cancellationToken) is not { IsActive: true })
        {
            return Invalid();
        }

        try
        {
            return await transaction.ExecuteAsync(ct => IssueAsync(request, activeTenantId, inviterId, recipient, requestedRoleIds, ct), cancellationToken);
        }
        catch (OfferAuthorityViolation)
        {
            // What this offer confers changed while it was being established, so the offer never happened.
            return Result<IssuedInvitation>.Failure(IdentityAccessErrors.InvitationConflict());
        }
        catch (DbUpdateConcurrencyException)
        {
            // The xmin token says another request settled this row first. That is the declared retryable
            // conflict (IA-REQ-035), not an unexpected failure.
            return Result<IssuedInvitation>.Failure(IdentityAccessErrors.InvitationConflict());
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // The partial unique index on a recipient's pending slot is the arbiter when two invitations race.
            // Whoever loses it is answered as a conflict rather than as an unhandled database failure.
            return Result<IssuedInvitation>.Failure(IdentityAccessErrors.InvitationConflict());
        }
    }

    private async Task<Result<IssuedInvitation>> IssueAsync(
        InviteMemberCommand request,
        TenantId tenantId,
        Guid inviterId,
        string recipient,
        Guid[] requestedRoleIds,
        CancellationToken cancellationToken)
    {
        var tenant = await context.Tenants.FirstOrDefaultAsync(candidate => candidate.Id == tenantId, cancellationToken);
        if (tenant is null || tenant.Type != TenantType.Organization || tenant.Status != TenantStatus.Active)
        {
            return Invalid();
        }

        // The offer has to resolve completely and stay inside the inviter's own authority (IA-REQ-014/047). Both
        // failures answer the same way: which role was refused, and why, is not the caller's to learn.
        var offer = await offerableRoles.ResolveAsync(tenantId, inviterId, requestedRoleIds, cancellationToken);
        if (!offer.IsOfferable)
        {
            return Invalid();
        }

        if (await HoldsMembershipAsync(tenantId, recipient, cancellationToken))
        {
            return Result<IssuedInvitation>.Failure(IdentityAccessErrors.InvitationConflict());
        }

        // Npgsql stores a timestamp to microsecond precision, so a clock reading with sub-microsecond ticks would
        // make the expiry this request reports differ from the expiry the database will actually enforce. The
        // window is truncated before it enters the aggregate, so the answer and the row agree exactly.
        var now = InvitationDelivery.ToStorablePrecision(timeProvider.GetUtcNow());
        var expiresAt = InvitationDelivery.ToStorablePrecision(now.Add(Window));
        var minted = InvitationDelivery.Mint(tokens, tokenHasher);
        var standing = await context.Invitations
            .Include(invitation => invitation.Roles)
            .FirstOrDefaultAsync(invitation => invitation.TenantId == tenantId
                && invitation.NormalizedEmail == recipient
                && invitation.Status == InvitationStatus.Pending, cancellationToken);

        // Captured BEFORE the aggregate rotates it. Reissue mutates the standing invitation in place, so reading
        // the hash afterwards would hand the retire step the NEW value and leave the previous envelope pending —
        // a token that no longer resolves but is still queued for delivery.
        var supersededHash = standing?.TokenHash;
        var invitation = Supersede(
            standing,
            tenant,
            recipient,
            offer.Roles,
            minted.Hash,
            requestLanguage.Language,
            now,
            expiresAt,
            requestedRoleIds);
        if (standing is null || !ReferenceEquals(standing, invitation))
        {
            context.Invitations.Add(invitation);
        }

        if (supersededHash is { } superseded)
        {
            await InvitationDelivery.RetireAsync(context, superseded, "invitation.superseded", now, cancellationToken);
        }

        InvitationDelivery.Deliver(context, secretWriter, minted, invitation.Id, tenantId, now, expiresAt);
        context.AuditEvents.Add(AuditEvent.Create(
            tenantId,
            inviterId,
            "invitation.issued",
            $"invitation-{invitation.Id.Value:N}",
            new Dictionary<string, string> { ["code"] = "invitation.issued", ["outcome"] = standing is null ? "issued" : "superseded" }));

        await context.SaveChangesAsync(cancellationToken);

        // The offer was authorized before this row existed, and while it did not exist no widening could see it
        // to cancel it. So the question is asked again here, after the write and under the lock: a widening
        // already in flight refuses this offer outright, and one that has not started cannot begin until this
        // transaction commits and the row becomes cancellable. Together they make IA-REQ-047 true at commit time
        // rather than only at validation time. The lock is never waited for — this transaction already holds the
        // rows a widening may be blocked on, and waiting here would be the other half of a deadlock.
        if (!await authorityLock.TryAcquireAsync(tenantId, cancellationToken) ||
            !(await offerableRoles.ResolveAsync(tenantId, inviterId, requestedRoleIds, cancellationToken)).IsOfferable)
        {
            // Refused by throwing, for the same reason the administrator floor is: the rows are already written
            // and this transaction commits unless something escapes it.
            throw new OfferAuthorityViolation();
        }

        return Result<IssuedInvitation>.Success(new IssuedInvitation(invitation.Id.Value, invitation.ExpiresAt));
    }

    /// <summary>
    /// The offer stopped being one the inviter could make, discovered after the write that must be undone. It
    /// never reaches a caller as an exception: <c>Handle</c> answers the declared retryable conflict.
    /// </summary>
    private sealed class OfferAuthorityViolation : Exception;

    /// <summary>
    /// A recipient holds at most one live offer (IA-REQ-017), so a second invitation supersedes the first rather
    /// than joining it. Reissuing in place covers the same offer made again; a different offer cannot be reissued,
    /// because the aggregate deliberately refuses to alter roles under a rotation, so it is withdrawn and replaced
    /// in the same transaction. Refusing instead would strand a caller: withdrawal needs another permission and has
    /// no route in the first increment.
    /// </summary>
    private static Invitation Supersede(
        Invitation? standing,
        Tenant tenant,
        string recipient,
        IReadOnlyCollection<Role> roles,
        VersionedTokenHash tokenHash,
        string language,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        Guid[] requestedRoleIds)
    {
        if (standing is null)
        {
            return Invitation.Issue(tenant, recipient, roles, tokenHash, language, now, expiresAt);
        }

        if (OffersExactly(standing, requestedRoleIds))
        {
            standing.Reissue(tenant, tokenHash, now, expiresAt);
            return standing;
        }

        standing.Cancel(tenant, now);
        return Invitation.Issue(tenant, recipient, roles, tokenHash, language, now, expiresAt);
    }

    private static bool OffersExactly(Invitation invitation, Guid[] requestedRoleIds) =>
        invitation.Roles.Count == requestedRoleIds.Length &&
        invitation.Roles.All(offered => requestedRoleIds.Contains(offered.RoleId.Value));

    /// <summary>
    /// An identity holds at most one membership per tenant (IA-REQ-016). Learning that at acceptance time would
    /// have spent an invitation and a delivery on an offer that could never settle.
    /// </summary>
    /// <summary>
    /// Whether this recipient already has a place here. A `Revoked` row is not one: it is the record that somebody
    /// was removed, and C5 makes a fresh invitation the only way back, so letting it refuse the offer would leave
    /// a removed member permanently unreachable (IA-REQ-053).
    /// </summary>
    private async Task<bool> HoldsMembershipAsync(TenantId tenantId, string recipient, CancellationToken cancellationToken)
    {
        var identity = await identities.FindByEmailAsync(recipient, CancellationToken.None);
        return identity is not null &&
            await context.TenantMemberships.AnyAsync(membership => membership.TenantId == tenantId
                && membership.IdentityId == identity.Id
                && membership.Status != MembershipStatus.Revoked, cancellationToken);
    }

    /// <summary>
    /// Whoever supersedes an offer owns invalidating what it replaced. A pending envelope left behind would let
    /// the delivery worker hand out a token that no longer resolves (IA-REQ-015/018).
    /// </summary>
    private async Task TerminalizeSupersededDeliveryAsync(Invitation? standing, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (standing is null)
        {
            return;
        }

        var supersededHash = standing.TokenHash.Value;
        var envelope = await context.OutboxSecrets
            .FirstOrDefaultAsync(secret => secret.VersionedHash == supersededHash && secret.Status == OutboxSecretStatus.Pending, cancellationToken);
        envelope?.Terminate(OutboxSecretStatus.Expired, "invitation.superseded", now);
    }

    /// <summary>
    /// Recognises PostgreSQL's unique violation without dragging the provider into the Application layer. The
    /// SQLSTATE is the stable contract; the exception type that carries it is Infrastructure's business.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is { } provider &&
        provider.GetType().GetProperty("SqlState")?.GetValue(provider) as string == "23505";

    private static DateTimeOffset ToStorablePrecision(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerMicrosecond), value.Offset);

    private static Result<IssuedInvitation> Invalid() => Result<IssuedInvitation>.Failure(IdentityAccessErrors.InvalidInvitation());

    private sealed record InvitationEnvelope(Guid InvitationId, Guid TenantId);
}
