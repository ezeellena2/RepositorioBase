using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.ExternalLogins;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.ExternalLogins;

/// <summary>
/// Where a handoff is created, and where the callback's own cookie says which one arrived back. The identifier
/// never reaches the client as something it can choose: the browser carries it in a short-lived cookie the
/// middleware's callback wrote, so a caller cannot name somebody else's handoff.
/// </summary>
public interface IExternalHandoffContext
{
    /// <summary>The handoff the current request's callback cookie names, if it names one at all.</summary>
    Guid? Current { get; }

    /// <summary>
    /// What that handoff is for, as the callback sealed it. The single completion route selects its request from
    /// this rather than from anything the caller sent, which is what keeps the purposes from crossing.
    /// </summary>
    ExternalAuthorizationPurpose? CurrentPurpose { get; }

    /// <summary>The provider names this deployment will start a challenge for.</summary>
    bool IsConfigured(string provider);

    /// <summary>
    /// All of them, so a screen can offer what exists instead of guessing. A client that guessed would offer a
    /// provider this deployment has no client for, and buy a password proof before finding out.
    /// </summary>
    IReadOnlyList<string> Configured { get; }
}

/// <summary>
/// Serializes the round trips that claim one provider account, whoever is claiming it.
/// <para>
/// It is deliberately not the session lock. That one is keyed on the identity, so two different identities
/// racing for the same provider account never meet, and the database unique key then refuses the loser by
/// throwing, in a transaction too far gone to record the refusal.
/// </para>
/// </summary>
public interface IExternalSubjectLock
{
    /// <summary>
    /// Takes the lock inside the ambient transaction, or answers <see langword="false"/> when the bounded wait
    /// elapses, which the caller answers as a retryable 429 and never as a decision about the subject.
    /// </summary>
    Task<bool> TryAcquireAsync(string provider, string subject, CancellationToken cancellationToken);
}

internal static class ExternalHandoff
{
    /// <summary>How long a person has to come back from the provider. A **product default**.</summary>
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Our own route, carrying nothing. Which handoff the browser is carrying is settled by the cookie the start
    /// endpoint sealed, so a caller cannot point this at a handoff somebody else started.
    /// </summary>
    internal static string ChallengeUri(string provider, string leg) =>
        $"/api/identity/external/{System.Uri.EscapeDataString(provider)}/{leg}/challenge";
}

public sealed class StartExternalLoginCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IExternalHandoffContext handoffs,
    TimeProvider timeProvider) : IRequestHandler<StartExternalLoginCommand, Result<ExternalAuthorizationHandoff>>
{
    public async Task<Result<ExternalAuthorizationHandoff>> Handle(StartExternalLoginCommand request, CancellationToken cancellationToken)
    {
        if (request.Provider is null || !handoffs.IsConfigured(request.Provider))
            return Result<ExternalAuthorizationHandoff>.Failure(IdentityAccessErrors.InvalidExternalLogin());

        return await transaction.ExecuteAsync(async ct =>
        {
            var handoff = ExternalAuthorizationRequest.Start(
                request.Provider, ExternalAuthorizationPurpose.Login, null, null, null, timeProvider.GetUtcNow(), ExternalHandoff.Lifetime);
            context.ExternalAuthorizationRequests.Add(handoff);
            context.AuditEvents.Add(AuditEvent.CreateSessionEvent(null, null, "identity.external.login.started", AuditCorrelation.Current(), "started"));
            await context.SaveChangesAsync(ct);
            return Result<ExternalAuthorizationHandoff>.Success(new ExternalAuthorizationHandoff(handoff.Id, ExternalAuthorizationPurpose.Login, ExternalHandoff.ChallengeUri(request.Provider, "login")));
        }, cancellationToken);
    }
}

public sealed class StartExternalLinkCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IExternalHandoffContext handoffs,
    IIdentityAccountService identities,
    IRecentIdentityProofStore proofs,
    IExternalIdentityService external,
    ICurrentSession currentSession,
    TimeProvider timeProvider) : IRequestHandler<StartExternalLinkCommand, Result<ExternalAuthorizationHandoff>>
{
    public async Task<Result<ExternalAuthorizationHandoff>> Handle(StartExternalLinkCommand request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.IdentityId is null || currentSession.SessionId is null)
            return Result<ExternalAuthorizationHandoff>.Failure(IdentityAccessErrors.InvalidSession());
        if (request.Provider is null || !handoffs.IsConfigured(request.Provider) || !request.Consent)
            return Result<ExternalAuthorizationHandoff>.Failure(IdentityAccessErrors.InvalidExternalLogin());

        var identityId = currentSession.IdentityId.Value;
        var identity = await identities.FindByIdAsync(identityId, cancellationToken);
        if (identity is null) return Result<ExternalAuthorizationHandoff>.Failure(IdentityAccessErrors.InvalidSession());
        if (!identity.IsActive) return Result<ExternalAuthorizationHandoff>.Failure(IdentityAccessErrors.EmailConfirmationRequired());
        if (await external.HasLinkAsync(identityId, request.Provider, cancellationToken))
            return Result<ExternalAuthorizationHandoff>.Failure(IdentityAccessErrors.ProviderAlreadyLinked());

        return await transaction.ExecuteAsync(async ct =>
        {
            // The proof is spent before the challenge is issued, not after it comes back: a person who cannot
            // prove it is still them should never reach the provider's consent screen at all.
            if (!await proofs.TryConsumeAsync(identityId, currentSession.SessionId.Value, ProofActions.ExternalLink, ct))
                return Result<ExternalAuthorizationHandoff>.Failure(IdentityAccessErrors.RecentProofRequired());

            var handoff = ExternalAuthorizationRequest.Start(
                request.Provider, ExternalAuthorizationPurpose.Link, identityId, currentSession.SessionId.Value, null, timeProvider.GetUtcNow(), ExternalHandoff.Lifetime);
            context.ExternalAuthorizationRequests.Add(handoff);
            await context.SaveChangesAsync(ct);
            return Result<ExternalAuthorizationHandoff>.Success(new ExternalAuthorizationHandoff(handoff.Id, ExternalAuthorizationPurpose.Link, ExternalHandoff.ChallengeUri(request.Provider, "link")));
        }, cancellationToken);
    }
}

public sealed class StartExternalProofCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IExternalHandoffContext handoffs,
    IExternalIdentityService external,
    ICurrentSession currentSession,
    TimeProvider timeProvider) : IRequestHandler<StartExternalProofCommand, Result<ExternalAuthorizationHandoff>>
{
    public async Task<Result<ExternalAuthorizationHandoff>> Handle(StartExternalProofCommand request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.IdentityId is null || currentSession.SessionId is null)
            return Result<ExternalAuthorizationHandoff>.Failure(IdentityAccessErrors.InvalidSession());
        if (request.Provider is null || !handoffs.IsConfigured(request.Provider) || request.Action is null || !ProofActions.All.Contains(request.Action))
            return Result<ExternalAuthorizationHandoff>.Failure(IdentityAccessErrors.InvalidExternalLogin());

        var identityId = currentSession.IdentityId.Value;

        // A provider can only prove it is still you if you already told us it is you. Without a link there is
        // nothing here to challenge, and answering anything else would be a route into an account through a
        // provider account nobody connected to it.
        if (!await external.HasLinkAsync(identityId, request.Provider, cancellationToken))
            return Result<ExternalAuthorizationHandoff>.Failure(IdentityAccessErrors.ExternalLinkNotFound());

        return await transaction.ExecuteAsync(async ct =>
        {
            var handoff = ExternalAuthorizationRequest.Start(
                request.Provider, ExternalAuthorizationPurpose.Proof, identityId, currentSession.SessionId.Value, request.Action, timeProvider.GetUtcNow(), ExternalHandoff.Lifetime);
            context.ExternalAuthorizationRequests.Add(handoff);
            await context.SaveChangesAsync(ct);
            return Result<ExternalAuthorizationHandoff>.Success(new ExternalAuthorizationHandoff(handoff.Id, ExternalAuthorizationPurpose.Proof, ExternalHandoff.ChallengeUri(request.Provider, "proof")));
        }, cancellationToken);
    }
}

public sealed class CompleteExternalLoginCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IExternalHandoffContext handoffs,
    IExternalIdentityService external,
    IIdentityAccountService identities,
    IExternalSubjectLock subjectLock,
    ISessionIssuer issuer,
    TimeProvider timeProvider) : IRequestHandler<CompleteExternalLoginCommand, Result<CompletedExternalLogin>>
{
    public async Task<Result<CompletedExternalLogin>> Handle(CompleteExternalLoginCommand request, CancellationToken cancellationToken)
    {
        return await transaction.ExecuteAsync(async ct =>
        {
            var handoff = await ExternalHandoffReader.LiveAsync(context, handoffs, ExternalAuthorizationPurpose.Login, timeProvider, ct);
            if (handoff is null) return Result<CompletedExternalLogin>.Failure(IdentityAccessErrors.InvalidExternalLogin());

            var now = timeProvider.GetUtcNow();
            if (!handoff.EmailVerified || string.IsNullOrWhiteSpace(handoff.ProviderEmail))
            {
                handoff.Fail(now);
                await context.SaveChangesAsync(ct);
                return Result<CompletedExternalLogin>.Failure(IdentityAccessErrors.InvalidExternalLogin());
            }

            // Taken before the subject is read, so what the read says is still true when the write happens. A
            // first sign-in creates an identity for this subject, and two of them racing would otherwise both
            // find it unowned.
            if (!await subjectLock.TryAcquireAsync(handoff.Provider, handoff.Subject!, ct))
                return Result<CompletedExternalLogin>.Failure(IdentityAccessErrors.SessionLockUnavailable());

            var linked = await external.FindIdentityBySubjectAsync(handoff.Provider, handoff.Subject!, ct);
            if (linked is null)
            {
                var email = handoff.ProviderEmail!.Trim().ToLowerInvariant();
                var existing = await identities.FindByEmailAsync(email, ct);

                // The forbidden path, and the whole reason BR-ID-005/006 exist: a matching address is not a proof
                // of ownership, so an unattended sign-in must not adopt a local identity because the two strings
                // happen to be equal. The person is told to sign in and link it themselves.
                if (existing is not null)
                {
                    handoff.Fail(now);
                    context.AuditEvents.Add(AuditEvent.CreateSessionEvent(null, null, "identity.external.login.refused", AuditCorrelation.Current(), "local_email_exists"));
                    await context.SaveChangesAsync(ct);
                    return Result<CompletedExternalLogin>.Failure(IdentityAccessErrors.ExternalLoginConflict());
                }

                var created = await external.CreateFromProviderAsync(email, ct);
                if (created is null)
                {
                    handoff.Fail(now);
                    await context.SaveChangesAsync(ct);
                    return Result<CompletedExternalLogin>.Failure(IdentityAccessErrors.ExternalLoginConflict());
                }

                if (!await external.LinkAsync(created.Value, handoff.Provider, handoff.Subject!, email, ct))
                {
                    handoff.Fail(now);
                    await context.SaveChangesAsync(ct);
                    return Result<CompletedExternalLogin>.Failure(IdentityAccessErrors.ExternalLoginConflict());
                }

                linked = created;
                context.AuditEvents.Add(AuditEvent.CreateSessionEvent(created, null, "identity.external.linked", AuditCorrelation.Current(), "created"));
            }

            var identity = await identities.FindByIdAsync(linked.Value, ct);
            if (identity is null || !identity.IsActive)
            {
                handoff.Fail(now);
                await context.SaveChangesAsync(ct);
                return Result<CompletedExternalLogin>.Failure(IdentityAccessErrors.InvalidExternalLogin());
            }

            var session = await issuer.IssueAsync(linked.Value, AuditCorrelation.Current(), ct);
            if (session is null) return Result<CompletedExternalLogin>.Failure(IdentityAccessErrors.SessionLockUnavailable());

            handoff.Consume(now);
            context.AuditEvents.Add(AuditEvent.CreateSessionEvent(linked.Value, session.Id.Value, "identity.external.login.succeeded", AuditCorrelation.Current(), "authenticated"));
            await context.SaveChangesAsync(ct);
            return Result<CompletedExternalLogin>.Success(new CompletedExternalLogin(linked.Value, session.Id.Value));
        }, cancellationToken);
    }
}

public sealed class CompleteExternalLinkCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IExternalHandoffContext handoffs,
    IExternalIdentityService external,
    IIdentityAccountService identities,
    IRecentIdentityProofStore proofs,
    ISessionLock identityLock,
    IExternalSubjectLock subjectLock,
    ICurrentSession currentSession,
    TimeProvider timeProvider) : IRequestHandler<CompleteExternalLinkCommand, Result>
{
    public async Task<Result> Handle(CompleteExternalLinkCommand request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.IdentityId is null)
            return Result.Failure(IdentityAccessErrors.InvalidSession());

        var identityId = currentSession.IdentityId.Value;
        return await transaction.ExecuteAsync(async ct =>
        {
            // The same per-identity lock a sign-in takes. Adding and removing an authenticator has to be
            // serialized against everything else that counts them, or two requests can each read a set that
            // the other is about to change.
            if (!await identityLock.TryAcquireAsync(identityId, ct)) return Result.Failure(IdentityAccessErrors.SessionLockUnavailable());

            var handoff = await ExternalHandoffReader.LiveAsync(context, handoffs, ExternalAuthorizationPurpose.Link, timeProvider, ct);
            if (handoff is null || handoff.IdentityId != identityId) return Result.Failure(IdentityAccessErrors.InvalidExternalLogin());

            var now = timeProvider.GetUtcNow();
            if (!handoff.EmailVerified || string.IsNullOrWhiteSpace(handoff.ProviderEmail))
            {
                handoff.Fail(now);
                await context.SaveChangesAsync(ct);
                return Result.Failure(IdentityAccessErrors.InvalidExternalLogin());
            }

            // Always after the identity lock, never before: one order everywhere is what keeps two callers from
            // waiting on each other. Without it two identities claiming one provider account both read it as
            // unowned, and the loser meets the database exception instead of this feature's conflict.
            if (!await subjectLock.TryAcquireAsync(handoff.Provider, handoff.Subject!, ct))
                return Result.Failure(IdentityAccessErrors.SessionLockUnavailable());

            var owner = await external.FindIdentityBySubjectAsync(handoff.Provider, handoff.Subject!, ct);
            if (owner == identityId)
            {
                // Already linked to this identity: idempotent, because a person pressing the button twice has
                // asked for a state that already holds.
                handoff.Consume(now);
                await context.SaveChangesAsync(ct);
                return Result.Success();
            }

            if (owner is not null) return await RefuseAsync(handoff, "subject_owned_elsewhere", now, ct);
            if (await external.HasLinkAsync(identityId, handoff.Provider, ct)) return await RefuseAsync(handoff, "provider_already_linked", now, ct);

            var providerEmail = handoff.ProviderEmail!.Trim().ToLowerInvariant();
            var identity = await identities.FindByIdAsync(identityId, ct);
            if (identity is null) return Result.Failure(IdentityAccessErrors.InvalidSession());

            // The address may be this identity's own — that is the ordinary case and it is allowed, because an
            // authenticated person linking their own account is not the automatic merge BR-ID-005/006 forbid. It
            // may not be somebody else's, which would give this identity a way in carrying another person's
            // address (amendment A2).
            if (!string.Equals(providerEmail, identity.Email, StringComparison.OrdinalIgnoreCase)
                && await identities.FindByEmailAsync(providerEmail, ct) is not null)
            {
                return await RefuseAsync(handoff, "local_email_exists", now, ct);
            }

            if (!await external.LinkAsync(identityId, handoff.Provider, handoff.Subject!, providerEmail, ct))
            {
                return await RefuseAsync(handoff, "subject_owned_elsewhere", now, ct);
            }

            handoff.Consume(now);

            // Linking is an authenticator change, so every outstanding proof dies with it and the other sessions
            // go: somebody who added a way into the account should not leave older sessions untouched.
            await proofs.AdvanceVersionAsync(identityId, ct);
            await CredentialSessionEffects.RevokeEveryLiveSessionAsync(context, identityId, currentSession.SessionId, now, "authenticator_linked", ct);
            context.AuditEvents.Add(AuditEvent.CreateSessionEvent(identityId, currentSession.SessionId?.Value, "identity.external.linked", AuditCorrelation.Current(), "linked"));
            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }

    private async Task<Result> RefuseAsync(ExternalAuthorizationRequest handoff, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        handoff.Fail(now);
        context.AuditEvents.Add(AuditEvent.CreateSessionEvent(handoff.IdentityId, null, "identity.external.link.refused", AuditCorrelation.Current(), reason));
        await context.SaveChangesAsync(cancellationToken);
        return Result.Failure(reason == "provider_already_linked"
            ? IdentityAccessErrors.ProviderAlreadyLinked()
            : IdentityAccessErrors.ExternalLoginConflict());
    }
}

public sealed class CompleteExternalProofCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IExternalHandoffContext handoffs,
    IExternalIdentityService external,
    IRecentIdentityProofStore proofs,
    ICurrentSession currentSession,
    TimeProvider timeProvider) : IRequestHandler<CompleteExternalProofCommand, Result>
{
    public async Task<Result> Handle(CompleteExternalProofCommand request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.IdentityId is null || currentSession.SessionId is null)
            return Result.Failure(IdentityAccessErrors.InvalidSession());

        var identityId = currentSession.IdentityId.Value;
        return await transaction.ExecuteAsync(async ct =>
        {
            var handoff = await ExternalHandoffReader.LiveAsync(context, handoffs, ExternalAuthorizationPurpose.Proof, timeProvider, ct);
            if (handoff is null || handoff.IdentityId != identityId || handoff.SessionId != currentSession.SessionId)
                return Result.Failure(IdentityAccessErrors.InvalidExternalLogin());

            var now = timeProvider.GetUtcNow();
            var owner = await external.FindIdentityBySubjectAsync(handoff.Provider, handoff.Subject ?? string.Empty, ct);

            // The subject has to be the one this identity linked. Proving with a different provider account is
            // proving something about somebody else.
            if (owner != identityId)
            {
                handoff.Fail(now);
                await context.SaveChangesAsync(ct);
                return Result.Failure(IdentityAccessErrors.InvalidExternalLogin());
            }

            await proofs.IssueAsync(identityId, currentSession.SessionId.Value, handoff.Action!, RecentIdentityProofMethod.ExternalProvider, ct);
            handoff.Consume(now);
            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }
}

public sealed class ListExternalLoginsQueryHandler(
    IExternalIdentityService external,
    IExternalHandoffContext handoffs,
    ICurrentSession currentSession) : IRequestHandler<ListExternalLoginsQuery, Result<ExternalLinkList>>
{
    public async Task<Result<ExternalLinkList>> Handle(ListExternalLoginsQuery request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.IdentityId is null)
            return Result<ExternalLinkList>.Failure(IdentityAccessErrors.InvalidSession());

        return Result<ExternalLinkList>.Success(new ExternalLinkList(
            await external.ListAsync(currentSession.IdentityId.Value, cancellationToken),
            handoffs.Configured));
    }
}

public sealed class UnlinkExternalLoginCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IExternalIdentityService external,
    IRecentIdentityProofStore proofs,
    ISessionLock identityLock,
    ICurrentSession currentSession) : IRequestHandler<UnlinkExternalLoginCommand, Result>
{
    public async Task<Result> Handle(UnlinkExternalLoginCommand request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.IdentityId is null || currentSession.SessionId is null)
            return Result.Failure(IdentityAccessErrors.InvalidSession());
        if (request.Provider is null) return Result.Failure(IdentityAccessErrors.ExternalLinkNotFound());

        var identityId = currentSession.IdentityId.Value;
        return await transaction.ExecuteAsync(async ct =>
        {
            if (!await identityLock.TryAcquireAsync(identityId, ct)) return Result.Failure(IdentityAccessErrors.SessionLockUnavailable());
            if (!await proofs.TryConsumeAsync(identityId, currentSession.SessionId.Value, ProofActions.ExternalUnlink, ct))
                return Result.Failure(IdentityAccessErrors.RecentProofRequired());
            if (!await external.HasLinkAsync(identityId, request.Provider, ct))
                return Result.Failure(IdentityAccessErrors.ExternalLinkNotFound());

            // Counted inside the transaction, after the lock the write will take: an unlink racing another unlink
            // must not both read "two authenticators" and both remove one.
            if (await external.AuthenticatorCountAsync(identityId, ct) <= 1)
                return Result.Failure(IdentityAccessErrors.LastAuthenticatorRequired());

            if (!await external.UnlinkAsync(identityId, request.Provider, ct))
                return Result.Failure(IdentityAccessErrors.ExternalLinkNotFound());

            await proofs.AdvanceVersionAsync(identityId, ct);
            context.AuditEvents.Add(AuditEvent.CreateSessionEvent(identityId, currentSession.SessionId.Value.Value, "identity.external.unlinked", AuditCorrelation.Current(), "unlinked"));
            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }
}

internal static class ExternalHandoffReader
{
    /// <summary>
    /// The handoff this request's callback cookie names, if it is validated, unexpired, and for the purpose the
    /// caller is asking about. A purpose mismatch answers nothing rather than the other purpose's effect.
    /// </summary>
    internal static async Task<ExternalAuthorizationRequest?> LiveAsync(
        IApplicationDbContext context,
        IExternalHandoffContext handoffs,
        ExternalAuthorizationPurpose purpose,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (handoffs.Current is not { } id) return null;
        var handoff = await context.ExternalAuthorizationRequests.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (handoff is null || handoff.Purpose != purpose) return null;
        return handoff.IsValidatedAt(timeProvider.GetUtcNow()) ? handoff : null;
    }
}
