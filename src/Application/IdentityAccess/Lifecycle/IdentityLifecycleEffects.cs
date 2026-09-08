using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Application.IdentityAccess.Credentials.PasswordRecovery;
using CleanArchitecture.Application.IdentityAccess.Platform;
using CleanArchitecture.Application.IdentityAccess.Roles;
using CleanArchitecture.Domain.IdentityAccess.Credentials;
using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Lifecycle;

/// <summary>
/// What stopping an account does to everything that was open in its name (IA-REQ-054).
/// <para>
/// It is one place rather than two because the person parking their own account and an operator suspending it
/// differ only in who decided and what is written in the audit. Everything else — the sessions, the proofs, the
/// links somebody is still holding — has to go both times, and two copies of that list would be two chances to
/// forget the same entry.
/// </para>
/// <para>
/// Memberships, invitations and profiles are deliberately untouched. Coming back should find the same
/// organizations, not a rebuilt approximation of them.
/// </para>
/// </summary>
internal static class IdentityLifecycleEffects
{
    internal static async Task ApplyDisableAsync(
        IApplicationDbContext context,
        IRecentIdentityProofStore proofs,
        Guid identityId,
        DateTimeOffset now,
        string outcome,
        CancellationToken cancellationToken)
    {
        await proofs.AdvanceVersionAsync(identityId, cancellationToken);
        await CredentialSessionEffects.RevokeEveryLiveSessionAsync(context, identityId, null, now, outcome, cancellationToken);
        await TerminalizeResetLinksAsync(context, identityId, now, outcome, cancellationToken);
        await TerminalizeReactivationTicketsAsync(context, identityId, now, outcome, cancellationToken);
        await ForgetPlatformStepUpAsync(context, identityId, cancellationToken);
    }

    /// <summary>
    /// Whether stopping this identity would leave the Platform with no active owner. A Platform nobody can
    /// administer has no higher authority to be restored from, so both routes that could produce one — the person
    /// parking their own account and an operator suspending it — ask the same question (IA-REQ-042, C6).
    /// </summary>
    internal static async Task<bool> WouldOrphanThePlatformAsync(
        IApplicationDbContext context,
        IPlatformMembershipActivator platformMemberships,
        Guid identityId,
        CancellationToken cancellationToken)
    {
        var platform = await context.Tenants.SingleOrDefaultAsync(
            tenant => tenant.Type == TenantType.Platform && tenant.Status == TenantStatus.Active, cancellationToken);
        if (platform is null) return false;

        var membership = await context.TenantMemberships.SingleOrDefaultAsync(
            candidate => candidate.TenantId == platform.Id
                && candidate.IdentityId == identityId
                && candidate.Status == MembershipStatus.Active,
            cancellationToken);

        return membership is not null && await platformMemberships.IsLastActiveOwnerAsync(platform, membership, cancellationToken);
    }

    /// <summary>
    /// Whether stopping this identity would leave an `Organization` it administers with nobody holding both
    /// halves of administration. It lives beside the Platform question and for the same reason: the person
    /// parking their own account and an operator suspending it produce the same hole, so they ask the same
    /// question rather than each carrying a copy of it (IA-REQ-053/054).
    /// <para>
    /// Asked before the state moves, because stopping an account leaves its membership rows untouched: the
    /// identity being stopped still counts as an administrator here, so the count has to be told to leave it out.
    /// </para>
    /// <para>
    /// The tenant lock is what makes the answer survive to the commit. A lock keyed on the identity — which both
    /// callers already hold, for the sessions they revoke — is the wrong key for the organization they might
    /// empty: two identities being stopped take two different keys, never meet, and each reads a world where the
    /// other is still an administrator. Keyed on the tenant, the second one waits and then counts a committed
    /// departure. It waits rather than failing fast because the answer matters: refusing for a busy lock would
    /// say "try again" when what is true is either "somebody else is still here" or "you are the last one".
    /// </para>
    /// <para>
    /// Ordering note for anything added later: both callers hold a session lock before asking for a tenant lock,
    /// and nothing in the codebase takes them the other way round.
    /// </para>
    /// </summary>
    internal static async Task<bool> WouldOrphanAnOrganizationAsync(
        IApplicationDbContext context,
        IRoleAdministrationStore roles,
        IRoleAuthorityLock authorityLock,
        Guid identityId,
        CancellationToken cancellationToken)
    {
        var tenants = await (
            from membership in context.TenantMemberships
            join tenant in context.Tenants on membership.TenantId equals tenant.Id
            where membership.IdentityId == identityId
                && membership.Status == MembershipStatus.Active
                && tenant.Type == TenantType.Organization
                && tenant.Status == TenantStatus.Active
            select membership.TenantId)
            .Distinct()
            // Ordered so two people leaving two shared organizations can never reach for them in opposite orders.
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);

        foreach (var tenantId in tenants)
        {
            await authorityLock.AcquireAsync(tenantId, cancellationToken);

            // Only an organization this person actually administers can be orphaned by their leaving. One where
            // they hold nothing loses nothing, and refusing there would strand somebody over a membership that
            // never conferred anything.
            if (await roles.CountAdministratorsAsync(tenantId, cancellationToken) == 0) continue;
            if (await roles.CountAdministratorsExceptAsync(tenantId, identityId, cancellationToken) == 0) return true;
        }

        return false;
    }

    /// <summary>
    /// A reset link mailed a minute before this would otherwise still open a password on an account nobody is
    /// supposed to be able to open.
    /// </summary>
    private static async Task TerminalizeResetLinksAsync(
        IApplicationDbContext context, Guid identityId, DateTimeOffset now, string reason, CancellationToken cancellationToken)
    {
        var pending = await context.PasswordResetRequests
            .Where(candidate => candidate.IdentityId == identityId && candidate.Status == PasswordResetStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var reset in pending)
        {
            reset.Supersede(now);
            await CredentialEnvelopes.TerminalizeAsync(
                context,
                RequestPasswordRecoveryCommandHandler.MessageType,
                JsonSerializer.Serialize(new RequestPasswordRecoveryCommandHandler.Envelope(reset.Id)),
                reason,
                now,
                cancellationToken);
        }
    }

    /// <summary>
    /// A ticket minted while somebody had parked their own account is not a way out of the suspension an operator
    /// imposed afterwards. The conditional state write would refuse it anyway; this makes it dead rather than
    /// merely refused, so nothing is relying on the order two writes happened to commit in.
    /// </summary>
    private static async Task TerminalizeReactivationTicketsAsync(
        IApplicationDbContext context, Guid identityId, DateTimeOffset now, string reason, CancellationToken cancellationToken)
    {
        var pending = await context.AccountReactivationRequests
            .Where(candidate => candidate.IdentityId == identityId && candidate.Status == AccountReactivationStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var ticket in pending)
        {
            ticket.Supersede(now);
            await CredentialEnvelopes.TerminalizeAsync(
                context,
                RequestAccountReactivationCommandHandler.MessageType,
                JsonSerializer.Serialize(new RequestAccountReactivationCommandHandler.Envelope(ticket.Id)),
                reason,
                now,
                cancellationToken);
        }
    }

    /// <summary>
    /// Evidence that a factor was proved should not outlive the account it was proved for, independently of
    /// whether the session holding it still exists.
    /// </summary>
    private static async Task ForgetPlatformStepUpAsync(IApplicationDbContext context, Guid identityId, CancellationToken cancellationToken)
    {
        var enrollment = await context.PlatformMfaEnrollments
            .SingleOrDefaultAsync(candidate => candidate.IdentityId == identityId, cancellationToken);
        enrollment?.ForgetStepUp();
    }
}

/// <summary>
/// Retiring the sealed envelope behind a settled intent. It is here rather than duplicated at each call site
/// because "the row is dead and so is the message carrying its token" is one rule, and two copies of it would be
/// two chances to forget the second half.
/// </summary>
internal static class CredentialEnvelopes
{
    internal static async Task TerminalizeAsync(
        IApplicationDbContext context,
        string messageType,
        string payload,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var message = await context.OutboxMessages
            .SingleOrDefaultAsync(candidate => candidate.Type == messageType && candidate.Payload == payload, cancellationToken);
        if (message is null) return;

        var secret = await context.OutboxSecrets.SingleOrDefaultAsync(candidate => candidate.OutboxMessageId == message.Id, cancellationToken);
        if (secret is null || secret.Status is OutboxSecretStatus.Consumed or OutboxSecretStatus.Failed or OutboxSecretStatus.Expired) return;
        secret.Terminate(OutboxSecretStatus.Failed, reason, now);
    }
}
