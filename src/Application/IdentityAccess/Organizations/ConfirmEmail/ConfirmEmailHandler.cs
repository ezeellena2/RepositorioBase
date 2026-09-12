using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Localization;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Invitations;
using CleanArchitecture.Application.IdentityAccess.Invitations.RegisterInvitedUser;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;

public sealed class ConfirmEmailCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IConfirmationSecretStore secrets,
    ITokenHasher tokenHasher,
    IIdentityAccountService identities,
    CleanArchitecture.Application.IdentityAccess.People.IPersonalDocumentRegistry personalDocuments,
    CleanArchitecture.Application.IdentityAccess.People.IIdentityDocumentProtector documentProtector,
    CleanArchitecture.Application.IdentityAccess.People.IIdentityDocumentFingerprint documentFingerprints,
    CleanArchitecture.Application.IdentityAccess.People.IPersonalDataMode personalDataMode,
    CleanArchitecture.Application.IdentityAccess.Security.ISharedAttemptBudget attemptBudget,
    IRegistrationIdempotencyStore idempotencyStore,
    IRegistrationInitialRoleProvisioner initialRoles,
    LocalizationSettings localization,
    TimeProvider timeProvider) : IRequestHandler<ConfirmEmailCommand, Result>
{
    private const string ConfirmationMessageType = "identity.confirmation.requested";

    public Task<Result> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        if (!ConfirmationToken.IsCanonical(request.Token)) return Task.FromResult(Result.Failure(IdentityAccessErrors.InvalidConfirmation()));
        return transaction.ExecuteAsync(async ct =>
        {
            var now = timeProvider.GetUtcNow();
            var hash = tokenHasher.Hash(request.Token);
            var secret = await secrets.GetByVersionedHashForUpdateAsync(hash, ct);
            if (secret is null || !tokenHasher.Verify(request.Token, secret.VersionedHash)) return Result.Failure(IdentityAccessErrors.InvalidConfirmation());
            // A spent envelope means the token did its work; what that work decided is recorded on the intent, not
            // on the envelope. Both terminal outcomes consume it, so answering success here would tell a person
            // whose registration was lost to a competing claim that it had succeeded — every time they clicked.
            if (secret.Status == OutboxSecretStatus.Consumed)
            {
                return await ResultForConsumedEnvelopeAsync(secret.OutboxMessageId, ct);
            }

            var message = await context.OutboxMessages.SingleOrDefaultAsync(item => item.Id == secret.OutboxMessageId, ct);
            if (message is null) return Result.Failure(IdentityAccessErrors.InvalidConfirmation());
            var purpose = Classify(message.Type);
            if (purpose == ConfirmationPurpose.Unknown) return Result.Failure(IdentityAccessErrors.InvalidConfirmation());

            // A terminal envelope can only name its refusal after its purpose is known. Organization registration
            // has a recorded conflict outcome; Personal and invitation links collapse every unusable state into
            // the same invalid-confirmation answer.
            if (secret.Status is not (OutboxSecretStatus.Pending or OutboxSecretStatus.Delivered))
            {
                return Result.Failure(TerminalErrorFor(purpose));
            }

            if (secret.ExpiresAt <= now)
            {
                secret.Terminate(OutboxSecretStatus.Expired, "confirmation_expired", now);
                await SettleExpiredIntentAsync(message, purpose, now, ct);
                await context.SaveChangesAsync(ct);
                return Result.Failure(TerminalErrorFor(purpose));
            }

            // An invited registration confirms an identity that holds no membership yet, so it writes its own
            // purpose. Confirming that purpose activates the identity and nothing else: acceptance is a separate
            // authenticated request and must stay the only thing that creates a membership (IA-REQ-016). The two
            // purposes are told apart by message type rather than by the shape of the payload, because a shape
            // test would silently match whichever envelope happened to deserialize.
            if (purpose == ConfirmationPurpose.InvitedIdentity)
            {
                if (!TryReadIdentityEnvelope(message.Payload, out var identityOnly)) return Result.Failure(IdentityAccessErrors.InvalidConfirmation());
                await identities.ActivateAsync(identityOnly.IdentityId, ct);
                secret.Consume("confirmation_consumed", now);
                context.AuditEvents.Add(AuditEvent.CreateIdentityConfirmed(identityOnly.IdentityId, null, $"confirmation-{secret.Id:N}", now));
                await context.SaveChangesAsync(ct);
                return Result.Success();
            }

            // The registration a person started anonymously. Spending this token is the proof that lets the
            // organization exist at all, so this is where every exclusive claim is made — and the first place any
            // of them could fail (IA-REQ-048).
            if (purpose == ConfirmationPurpose.OrganizationIntent)
            {
                return await FinalizeRegistrationAsync(message.Payload, secret, now, ct);
            }

            // The same proof, for a person instead of a company. It is the same endpoint and the same envelope
            // because a confirmation link is a confirmation link (IA-REQ-005); only what it finalizes differs.
            if (purpose == ConfirmationPurpose.PersonalIntent)
            {
                return await FinalizePersonalAsync(message.Payload, secret, now, ct);
            }

            if (!TryReadEnvelope(message.Payload, out var envelope)) return Result.Failure(IdentityAccessErrors.InvalidConfirmation());
            var tenant = await context.Tenants.SingleOrDefaultAsync(item => item.Id == TenantId.From(envelope.TenantId), ct);
            var membership = await context.TenantMemberships.SingleOrDefaultAsync(item => item.Id == MembershipId.From(envelope.MembershipId), ct);
            if (tenant is null || membership is null || membership.TenantId != tenant.Id || membership.IdentityId != envelope.IdentityId)
                return Result.Failure(IdentityAccessErrors.InvalidConfirmation());
            if (tenant.Status != TenantStatus.PendingConfirmation || membership.Status != MembershipStatus.PendingConfirmation)
                return Result.Failure(IdentityAccessErrors.RegistrationConflict());

            await identities.ActivateAsync(envelope.IdentityId, ct);
            tenant.Activate();
            membership.Activate(tenant);

            // The organization's one owner, named the instant it has an active responsible member. Owning
            // requires an active membership, so this cannot happen any earlier than here (IA-REQ-053).
            tenant.TransferOwnershipTo(membership);
            secret.Consume("confirmation_consumed", now);
            context.AuditEvents.Add(AuditEvent.Create(tenant.Id, envelope.IdentityId, "identity.confirmed", $"confirmation-{secret.Id:N}", new Dictionary<string, string> { ["code"] = "identity.confirmed", ["outcome"] = "activated" }));
            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }

    /// <summary>
    /// Turns a proved intent into the organization it asked for, or records honestly why it cannot.
    /// <para>
    /// The order matters and is the whole contract. The business lock is taken first, so two intents naming one
    /// CUIT are serialized rather than racing the unique index. The identity is created before the CUIT is
    /// checked, because someone who proved control of an address has earned their account even when the
    /// organization is lost — they can sign in and register another one. The address is re-checked under the lock,
    /// because an unrelated registration may have created it while this token sat in a mailbox, and in that case
    /// nothing is created and the submitted password is never applied to an account this caller may not own.
    /// </para>
    /// </summary>
    private async Task<Result> FinalizeRegistrationAsync(string payload, OutboxSecret secret, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!TryReadIntentEnvelope(payload, out var envelope)) return Result.Failure(IdentityAccessErrors.InvalidConfirmation());

        var intent = await context.PendingRegistrationIntents.SingleOrDefaultAsync(item => item.Id == envelope.IntentId, cancellationToken);
        if (intent is null) return Result.Failure(IdentityAccessErrors.InvalidConfirmation());

        // A settled intent answers what it settled as. The envelope's own single-use lock already makes the first
        // spend the winner; this is what that winner recorded.
        if (intent.Outcome is { } settled) return ResultFor(settled);

        await idempotencyStore.CoordinateBusinessIntentAsync(intent.NormalizedEmail, intent.Cuit.Value, cancellationToken);

        var correlation = $"registration-intent-{intent.Id:N}";
        if (await identities.FindByEmailAsync(intent.NormalizedEmail, cancellationToken) is not null)
        {
            return await SettleConflictAsync(intent, secret, correlation, "identity_exists", null, now, cancellationToken);
        }

        var creation = await identities.CreatePendingFromHashAsync(
            intent.NormalizedEmail,
            intent.PasswordHash,
            intent.Language ?? localization.DefaultLanguage,
            cancellationToken);
        if (creation.IsValidationFailure || creation.Account is null)
        {
            return await SettleConflictAsync(intent, secret, correlation, "identity_exists", null, now, cancellationToken);
        }

        var identityId = creation.Account.Id;
        await identities.ActivateAsync(identityId, cancellationToken);

        if (await context.OrganizationProfiles.AnyAsync(profile => profile.Cuit == intent.Cuit, cancellationToken))
        {
            // The account stands; only the organization is lost. This is the honest post-proof conflict: the
            // person owns a usable, confirmed account and can register a different organization.
            return await SettleConflictAsync(intent, secret, correlation, "cuit_taken", identityId, now, cancellationToken);
        }

        var tenant = Tenant.CreateOrganization(TenantSlug.From($"org-{intent.Cuit.Value}"));
        var membership = TenantMembership.CreateResponsible(tenant, identityId);
        await initialRoles.AssignResponsibleOwnerAsync(tenant, membership, cancellationToken);
        tenant.Activate();
        membership.Activate(tenant);

        context.Tenants.Add(tenant);
        context.OrganizationProfiles.Add(OrganizationProfile.Create(tenant, intent.LegalName, intent.Cuit));
        context.TenantMemberships.Add(membership);

        // The tenant points at the membership that owns it and the membership points back at the tenant, so the
        // two rows cannot be inserted in one statement ordering. They are written first and the owner named
        // second, inside this same transaction — which is why the column is nullable and why "an organization
        // always has an owner" is an application invariant rather than a CHECK (IA-REQ-053).
        await context.SaveChangesAsync(cancellationToken);
        tenant.TransferOwnershipTo(membership);

        intent.Complete(PendingRegistrationIntentOutcome.Created, now);
        secret.Consume("confirmation_consumed", now);
        context.AuditEvents.Add(AuditEvent.Create(tenant.Id, identityId, "organization.registration.requested", correlation, new Dictionary<string, string>
        {
            ["code"] = "organization.registration.requested",
            ["outcome"] = "registered"
        }));
        context.AuditEvents.Add(AuditEvent.CreateIdentityConfirmed(identityId, tenant.Id, correlation, now));
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Turns a proved personal intent into the context it asked for.
    /// <para>
    /// The order is the organization finalization's, with one addition: the claim budget is spent before anything
    /// is looked at, because a refused claim must still cost an attempt. As there, the identity is created before
    /// the documentary identity is checked — somebody who proved their own address has earned their account even
    /// when the document is already recorded, and they can then correct it through the process Task 26 builds.
    /// </para>
    /// </summary>
    private async Task<Result> FinalizePersonalAsync(string payload, OutboxSecret secret, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!TryReadPersonalEnvelope(payload, out var envelope)) return Result.Failure(IdentityAccessErrors.InvalidConfirmation());

        var intent = await context.PendingPersonalIntents.SingleOrDefaultAsync(item => item.Id == envelope.IntentId, cancellationToken);
        if (intent is null) return Result.Failure(IdentityAccessErrors.InvalidConfirmation());
        if (intent.Outcome is { } settled) return PersonalResultFor(settled);

        var decision = await attemptBudget.SpendAsync(
            CleanArchitecture.Application.IdentityAccess.People.PersonalAttemptBudgets.DocumentClaim,
            intent.NormalizedEmail,
            cancellationToken);
        if (BudgetRefusal(decision) is { } refusal) return Result.Failure(refusal);

        var document = documentProtector.Reveal(intent.DocumentCiphertext);
        if (document is null)
        {
            // The envelope outlived the key that sealed it. Nothing can be created from it, and it is settled so
            // the same dead link cannot keep asking.
            return await SettlePersonalConflictAsync(intent, secret, null, now, cancellationToken);
        }

        var fingerprints = documentFingerprints.ForRetainedKeys(document.Value);
        var correlation = $"personal-intent-{intent.Id:N}";
        await idempotencyStore.CoordinateBusinessIntentAsync(intent.NormalizedEmail, fingerprints[0].Value, cancellationToken);

        if (await identities.FindByEmailAsync(intent.NormalizedEmail, cancellationToken) is not null)
        {
            return await SettlePersonalConflictAsync(intent, secret, null, now, cancellationToken);
        }

        var creation = await identities.CreatePendingFromHashAsync(
            intent.NormalizedEmail,
            intent.PasswordHash,
            intent.Language ?? localization.DefaultLanguage,
            cancellationToken);
        if (creation.IsValidationFailure || creation.Account is null)
        {
            return await SettlePersonalConflictAsync(intent, secret, null, now, cancellationToken);
        }

        var identityId = creation.Account.Id;
        await identities.ActivateAsync(identityId, cancellationToken);

        if (await personalDocuments.IsRecordedAsync(fingerprints, cancellationToken))
        {
            // The account stands; only the context is lost, and the person is told the one thing every refused
            // claim is told. That code says nothing about whose document it is (SPEC section 14.3).
            return await SettlePersonalConflictAsync(intent, secret, identityId, now, cancellationToken);
        }

        CleanArchitecture.Application.IdentityAccess.People.PersonalContextFactory.Add(
            context,
            identityId,
            intent.FullName,
            intent.DisplayName,
            intent.DocumentCiphertext,
            fingerprints,
            personalDataMode.Classification,
            correlation,
            now);

        intent.Complete(PendingRegistrationIntentOutcome.Created, now);
        secret.Consume("confirmation_consumed", now);
        context.AuditEvents.Add(AuditEvent.CreateIdentityConfirmed(identityId, null, correlation, now));
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Result> SettlePersonalConflictAsync(
        CleanArchitecture.Domain.IdentityAccess.People.PendingPersonalIntent intent,
        OutboxSecret secret,
        Guid? actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        intent.Complete(PendingRegistrationIntentOutcome.Conflicted, now);
        secret.Consume("confirmation_consumed", now);
        var correlation = $"personal-intent-{intent.Id:N}";
        context.AuditEvents.Add(AuditEvent.CreateRegistrationConflicted(correlation, "personal_conflict", actorId, now));
        if (actorId is { } identityId) context.AuditEvents.Add(AuditEvent.CreateIdentityConfirmed(identityId, null, correlation, now));
        await context.SaveChangesAsync(cancellationToken);
        return Result.Failure(IdentityAccessErrors.PersonalRegistrationConflict());
    }

    private static Result PersonalResultFor(PendingRegistrationIntentOutcome outcome) => outcome switch
    {
        PendingRegistrationIntentOutcome.Created => Result.Success(),
        _ => Result.Failure(IdentityAccessErrors.PersonalRegistrationConflict())
    };

    private static ApplicationError? BudgetRefusal(CleanArchitecture.Application.IdentityAccess.Security.AttemptBudgetDecision decision) =>
        decision.Outcome switch
        {
            CleanArchitecture.Application.IdentityAccess.Security.AttemptBudgetOutcome.Admitted => null,
            CleanArchitecture.Application.IdentityAccess.Security.AttemptBudgetOutcome.Exhausted =>
                IdentityAccessErrors.AttemptsExhausted(Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalSeconds))),
            _ => IdentityAccessErrors.ServiceUnavailable(Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalSeconds)))
        };

    private static bool TryReadPersonalEnvelope(string payload, out CleanArchitecture.Application.IdentityAccess.People.RegisterPersonal.RegisterPersonalCommandHandler.IntentEnvelope envelope)
    {
        envelope = default!;
        try
        {
            var value = JsonSerializer.Deserialize<CleanArchitecture.Application.IdentityAccess.People.RegisterPersonal.RegisterPersonalCommandHandler.IntentEnvelope>(payload);
            if (value is null || value.IntentId == Guid.Empty) return false;
            envelope = value;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Settles an intent that cannot become an organization. The token is spent either way, because a conflict is
    /// a real outcome and not an invitation to retry the same link until the state changes.
    /// </summary>
    private async Task<Result> SettleConflictAsync(
        PendingRegistrationIntent intent,
        OutboxSecret secret,
        string correlation,
        string outcome,
        Guid? actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        intent.Complete(PendingRegistrationIntentOutcome.Conflicted, now);
        secret.Consume("confirmation_consumed", now);
        context.AuditEvents.Add(AuditEvent.CreateRegistrationConflicted(correlation, outcome, actorId, now));
        if (actorId is { } identityId) context.AuditEvents.Add(AuditEvent.CreateIdentityConfirmed(identityId, null, correlation, now));
        await context.SaveChangesAsync(cancellationToken);
        return Result.Failure(IdentityAccessErrors.RegistrationConflict());
    }

    private static Result ResultFor(PendingRegistrationIntentOutcome outcome) => outcome switch
    {
        PendingRegistrationIntentOutcome.Created => Result.Success(),
        _ => Result.Failure(IdentityAccessErrors.RegistrationConflict())
    };

    /// <summary>Settles only the intent carried by the classified envelope; legacy and invited confirmations have none.</summary>
    private async Task SettleExpiredIntentAsync(
        OutboxMessage message,
        ConfirmationPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (purpose == ConfirmationPurpose.OrganizationIntent && TryReadIntentEnvelope(message.Payload, out var organizationEnvelope))
        {
            var intent = await context.PendingRegistrationIntents
                .SingleOrDefaultAsync(item => item.Id == organizationEnvelope.IntentId, cancellationToken);
            if (intent is { Outcome: null }) intent.Complete(PendingRegistrationIntentOutcome.Expired, now);
            return;
        }

        if (purpose == ConfirmationPurpose.PersonalIntent && TryReadPersonalEnvelope(message.Payload, out var personalEnvelope))
        {
            var intent = await context.PendingPersonalIntents
                .SingleOrDefaultAsync(item => item.Id == personalEnvelope.IntentId, cancellationToken);
            if (intent is { Outcome: null }) intent.Complete(PendingRegistrationIntentOutcome.Expired, now);
        }
    }

    /// <summary>Returns the outcome a spent registration envelope recorded, or the legacy success fallback.</summary>
    private async Task<Result> ResultForConsumedEnvelopeAsync(Guid outboxMessageId, CancellationToken cancellationToken)
    {
        var message = await context.OutboxMessages.AsNoTracking().SingleOrDefaultAsync(item => item.Id == outboxMessageId, cancellationToken);
        if (message is null) return Result.Success();

        if (string.Equals(message.Type, RegisterOrganizationCommandHandler.IntentConfirmationMessageType, StringComparison.Ordinal)
            && TryReadIntentEnvelope(message.Payload, out var organizationEnvelope))
        {
            var outcome = await context.PendingRegistrationIntents
                .AsNoTracking()
                .Where(item => item.Id == organizationEnvelope.IntentId)
                .Select(item => item.Outcome)
                .SingleOrDefaultAsync(cancellationToken);
            return outcome is { } recorded ? ResultFor(recorded) : Result.Success();
        }

        if (string.Equals(message.Type, CleanArchitecture.Application.IdentityAccess.People.RegisterPersonal.RegisterPersonalCommandHandler.IntentConfirmationMessageType, StringComparison.Ordinal)
            && TryReadPersonalEnvelope(message.Payload, out var personalEnvelope))
        {
            var outcome = await context.PendingPersonalIntents
                .AsNoTracking()
                .Where(item => item.Id == personalEnvelope.IntentId)
                .Select(item => item.Outcome)
                .SingleOrDefaultAsync(cancellationToken);
            return outcome is { } recorded ? PersonalResultFor(recorded) : Result.Success();
        }

        return Result.Success();
    }

    private static ConfirmationPurpose Classify(string? messageType) => messageType switch
    {
        RegisterOrganizationCommandHandler.IntentConfirmationMessageType => ConfirmationPurpose.OrganizationIntent,
        ConfirmationMessageType => ConfirmationPurpose.LegacyOrganization,
        CleanArchitecture.Application.IdentityAccess.People.RegisterPersonal.RegisterPersonalCommandHandler.IntentConfirmationMessageType => ConfirmationPurpose.PersonalIntent,
        RegisterInvitedUserCommandHandler.InvitedConfirmationMessageType => ConfirmationPurpose.InvitedIdentity,
        _ => ConfirmationPurpose.Unknown
    };

    private static ApplicationError TerminalErrorFor(ConfirmationPurpose purpose) =>
        purpose is ConfirmationPurpose.OrganizationIntent or ConfirmationPurpose.LegacyOrganization
            ? IdentityAccessErrors.RegistrationConflict()
            : IdentityAccessErrors.InvalidConfirmation();

    private static bool TryReadIntentEnvelope(string payload, out RegisterOrganizationCommandHandler.IntentEnvelope envelope)
    {
        envelope = default!;
        try
        {
            var value = JsonSerializer.Deserialize<RegisterOrganizationCommandHandler.IntentEnvelope>(payload);
            if (value is null || value.IntentId == Guid.Empty) return false;
            envelope = value;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadEnvelope(string payload, out ConfirmationEnvelope envelope)
    {
        envelope = default!;
        try
        {
            var value = JsonSerializer.Deserialize<ConfirmationEnvelope>(payload);
            if (value is null || value.IdentityId == Guid.Empty || value.TenantId == Guid.Empty || value.MembershipId == Guid.Empty) return false;
            envelope = value;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Reads the identity-only envelope, once its message type has already established the purpose.</summary>
    private static bool TryReadIdentityEnvelope(string payload, out RegisterInvitedUserCommandHandler.IdentityConfirmationEnvelope envelope)
    {
        envelope = default!;
        try
        {
            var value = JsonSerializer.Deserialize<RegisterInvitedUserCommandHandler.IdentityConfirmationEnvelope>(payload);
            if (value is null || value.IdentityId == Guid.Empty) return false;
            envelope = value;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record ConfirmationEnvelope(Guid IdentityId, Guid TenantId, Guid MembershipId);

    private enum ConfirmationPurpose
    {
        Unknown,
        OrganizationIntent,
        LegacyOrganization,
        PersonalIntent,
        InvitedIdentity
    }
}
