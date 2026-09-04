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
        if (currentTenant.TenantId is not { } activeTenantId || activeTenantId != request.TenantId)
        {
            return Invalid();
        }

        if (user.Id is not { } inviterId || inviterId == Guid.Empty)
        {
            return Invalid();
        }

        if (!TryNormalizeRecipient(request.Email, out var recipient))
        {
            return Invalid();
        }

        var requestedRoleIds = request.RoleIds?.Distinct().ToArray() ?? [];
        if (requestedRoleIds.Length == 0)
        {
            return Invalid();
        }

        // IA-REQ-005. The pipeline proves the permission, never the confirmation, so this gate is the handler's.
        if (await identities.FindByIdAsync(inviterId, cancellationToken) is not { IsActive: true })
        {
            return Invalid();
        }

        try
        {
            return await transaction.ExecuteAsync(ct => IssueAsync(request, activeTenantId, inviterId, recipient, requestedRoleIds, ct), cancellationToken);
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
        var now = ToStorablePrecision(timeProvider.GetUtcNow());
        var expiresAt = ToStorablePrecision(now.Add(Window));
        var rawToken = tokens.Generate();
        var tokenHash = VersionedTokenHash.FromPersistedValue(tokenHasher.Hash(rawToken));

        var standing = await context.Invitations
            .Include(invitation => invitation.Roles)
            .FirstOrDefaultAsync(invitation => invitation.TenantId == tenantId
                && invitation.NormalizedEmail == recipient
                && invitation.Status == InvitationStatus.Pending, cancellationToken);

        var invitation = Supersede(standing, tenant, recipient, offer.Roles, tokenHash, now, expiresAt, requestedRoleIds);
        if (standing is null || !ReferenceEquals(standing, invitation))
        {
            context.Invitations.Add(invitation);
        }

        await TerminalizeSupersededDeliveryAsync(standing, now, cancellationToken);

        var outbox = OutboxMessage.Create(MessageType, JsonSerializer.Serialize(new InvitationEnvelope(invitation.Id.Value, tenantId.Value)), now);
        context.OutboxMessages.Add(outbox);
        context.OutboxSecrets.Add(OutboxSecret.Create(outbox.Id, tokenHash.Value, secretWriter.Encrypt(rawToken), expiresAt));
        context.AuditEvents.Add(AuditEvent.Create(
            tenantId,
            inviterId,
            "invitation.issued",
            $"invitation-{invitation.Id.Value:N}",
            new Dictionary<string, string> { ["code"] = "invitation.issued", ["outcome"] = standing is null ? "issued" : "superseded" }));

        await context.SaveChangesAsync(cancellationToken);
        return Result<IssuedInvitation>.Success(new IssuedInvitation(invitation.Id.Value, invitation.ExpiresAt));
    }

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
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        Guid[] requestedRoleIds)
    {
        if (standing is null)
        {
            return Invitation.Issue(tenant, recipient, roles, tokenHash, now, expiresAt);
        }

        if (OffersExactly(standing, requestedRoleIds))
        {
            standing.Reissue(tenant, tokenHash, now, expiresAt);
            return standing;
        }

        standing.Cancel(tenant, now);
        return Invitation.Issue(tenant, recipient, roles, tokenHash, now, expiresAt);
    }

    private static bool OffersExactly(Invitation invitation, Guid[] requestedRoleIds) =>
        invitation.Roles.Count == requestedRoleIds.Length &&
        invitation.Roles.All(offered => requestedRoleIds.Contains(offered.RoleId.Value));

    /// <summary>
    /// An identity holds at most one membership per tenant (IA-REQ-016). Learning that at acceptance time would
    /// have spent an invitation and a delivery on an offer that could never settle.
    /// </summary>
    private async Task<bool> HoldsMembershipAsync(TenantId tenantId, string recipient, CancellationToken cancellationToken)
    {
        var identity = await identities.FindByEmailAsync(recipient, CancellationToken.None);
        return identity is not null &&
            await context.TenantMemberships.AnyAsync(membership => membership.TenantId == tenantId && membership.IdentityId == identity.Id, cancellationToken);
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

    private static bool TryNormalizeRecipient(string? email, out string recipient)
    {
        recipient = string.Empty;
        if (email is null || email.Length > 256)
        {
            return false;
        }

        try
        {
            recipient = Invitation.Canonicalize(email);
            return true;
        }
        catch (ArgumentException)
        {
            // The aggregate is the authority on what a recipient is; a value it refuses is bad input, not a fault.
            return false;
        }
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
