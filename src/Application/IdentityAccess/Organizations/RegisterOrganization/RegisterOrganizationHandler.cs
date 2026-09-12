using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;

public sealed class RegisterOrganizationCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IRegistrationIdempotencyStore idempotencyStore,
    IIdentityAccountService identities,
    IValidatedOptionalSession session,
    ISecureTokenGenerator tokens,
    ITokenHasher tokenHasher,
    IOutboxSecretWriter secretWriter,
    IRegistrationInitialRoleProvisioner initialRoles,
    IRequestLanguage requestLanguage,
    TimeProvider timeProvider) : IRequestHandler<RegisterOrganizationCommand, Result>
{
    /// <summary>Carries the intent's confirmation token to the address that must prove it owns itself.</summary>
    public const string IntentConfirmationMessageType = "identity.registration.confirmation.requested";

    /// <summary>The tokenless notice an address that already has an account receives instead.</summary>
    public const string IntentSignInNoticeMessageType = "identity.registration.signin.notice.requested";

    /// <summary>
    /// How long an unproved initiation stays finalizable. It matches the sealed envelope's own window so a person
    /// meets one deadline rather than two; the envelope's expiry is the one that actually refuses a late token.
    /// </summary>
    private static readonly TimeSpan IntentWindow = TimeSpan.FromHours(24);

    /// <summary>Named only by its identifier: the payload is stored in the clear and holds no address or CUIT.</summary>
    public sealed record IntentEnvelope(Guid IntentId);

    public async Task<Result> Handle(RegisterOrganizationCommand request, CancellationToken cancellationToken)
    {
        if (session.IsInvalid || session.IdentityId.HasValue != (session.Email is not null)) return Result.Failure(IdentityAccessErrors.InvalidSession());
        if (!TryNormalize(request, out var intent)) return Result.Failure(IdentityAccessErrors.InvalidRegistration());

        // Password policy depends on the submitted password alone, so it is decided here — before any address is
        // looked up. Validating it only for a free address made a weak password answer invalid_registration for an
        // untaken address and neutrally succeed for a taken one, which is an enumeration oracle anyone could probe.
        if (session.IdentityId is null)
        {
            var password = await identities.ValidatePasswordAsync(request.Password, cancellationToken);
            if (!password.IsValid)
            {
                return Result.Failure(IdentityAccessErrors.PasswordPolicyFailed(
                    new Dictionary<string, ValidationErrorDetail[]> { ["password"] = password.Errors.ToArray() }));
            }
        }

        return await transaction.ExecuteAsync(async ct =>
            {
                var scope = session.IdentityId?.ToString("N") ?? "anonymous";
                var canonicalKey = CanonicalKey(scope, intent);
                var claim = await idempotencyStore.TryClaimAsync(canonicalKey, ct);
                if (!claim.IsOwner) return Replay(claim.Submission);

                var submission = claim.Submission;

                // The anonymous phase reserves nothing, so it takes no business lock and asks no question whose
                // answer could vary the work it does. Everything below this branch belongs to a caller whose
                // identity is already proved by their session (IA-REQ-048).
                if (session.IdentityId is null)
                {
                    await InitiateAsync(request, intent, submission, ct);
                    return await CompleteSubmissionAsync(submission, RegistrationSubmissionOutcome.Accepted, ct);
                }

                await idempotencyStore.CoordinateBusinessIntentAsync(intent.Email, intent.Cuit.Value, ct);
                // A validated session belongs to an `Active` identity by construction: the cookie's own
                // validation refuses anything else, so this is naming what the session already proved rather
                // than deciding it here (IA-REQ-054).
                var identity = new IdentityAccount(session.IdentityId.Value, intent.Email, IdentityAccountStatus.Active);

                // A signed-in caller may only register for the address their own session proves, so nothing here
                // can be varied to probe someone else. Telling them the CUIT is already registered is the useful
                // answer, and it reveals nothing about any identity — and it can no longer be composed with an
                // anonymous probe, because the anonymous phase now leaves no claim for this answer to depend on.
                if (await context.OrganizationProfiles.AnyAsync(profile => profile.Cuit == intent.Cuit, ct))
                {
                    return await CompleteSubmissionAsync(submission, RegistrationSubmissionOutcome.RegistrationConflict, ct);
                }

                var tenant = Tenant.CreateOrganization(TenantSlug.From($"org-{intent.Cuit.Value}"));
                var membership = TenantMembership.CreateResponsible(tenant, identity.Id);
                await initialRoles.AssignResponsibleOwnerAsync(tenant, membership, ct);
                var organization = OrganizationProfile.Create(tenant, intent.LegalName, intent.Cuit);
                var rawToken = tokens.Generate();
                var now = timeProvider.GetUtcNow();
                var outbox = OutboxMessage.Create("identity.confirmation.requested", JsonSerializer.Serialize(new ConfirmationEnvelope(identity.Id, tenant.Id.Value, membership.Id.Value)), now);
                var secret = OutboxSecret.Create(outbox.Id, tokenHasher.Hash(rawToken), secretWriter.Encrypt(rawToken), now.AddHours(24));

                context.Tenants.Add(tenant);
                context.OrganizationProfiles.Add(organization);
                context.TenantMemberships.Add(membership);
                context.OutboxMessages.Add(outbox);
                context.OutboxSecrets.Add(secret);
                context.AuditEvents.Add(AuditEvent.Create(tenant.Id, identity.Id, "organization.registration.requested", Correlation(canonicalKey), new Dictionary<string, string> { ["code"] = "organization.registration.requested", ["outcome"] = "pending_confirmation" }));
                await context.SaveChangesAsync(ct);
                return await CompleteSubmissionAsync(submission, RegistrationSubmissionOutcome.Accepted, ct);
            }, cancellationToken);
    }

    /// <summary>
    /// The anonymous phase. It writes one intent, one outbox message and one tenantless audit event, and it writes
    /// the same number of each whether or not the address has an account and whether or not the CUIT is taken —
    /// only the message differs, and only its own recipient can read that. It creates no identity, no tenant, no
    /// profile, no membership and no role, so nothing it leaves behind can refuse or answer another caller
    /// (IA-REQ-003/048).
    /// </summary>
    private async Task InitiateAsync(RegisterOrganizationCommand request, RegistrationIntent intent, RegistrationSubmission submission, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(IntentWindow);
        var existing = await identities.FindByEmailAsync(intent.Email, cancellationToken);

        // The password is hashed on every anonymous request, before the address is looked up, so neither the answer
        // nor the work done varies with what the system knows about it. The plaintext is not stored.
        var passwordHash = identities.HashPassword(request.Password);

        PendingRegistrationIntent pending;
        OutboxMessage outbox;
        if (existing is null)
        {
            pending = PendingRegistrationIntent.Open(
                submission.Id,
                intent.Email,
                intent.LegalName,
                intent.Cuit,
                passwordHash,
                requestLanguage.Language,
                now,
                expiresAt);
            outbox = OutboxMessage.Create(IntentConfirmationMessageType, JsonSerializer.Serialize(new IntentEnvelope(pending.Id)), now);
            var rawToken = tokens.Generate();
            context.OutboxSecrets.Add(OutboxSecret.Create(outbox.Id, tokenHasher.Hash(rawToken), secretWriter.Encrypt(rawToken), expiresAt));
        }
        else
        {
            // The address owner is told they can sign in. The notice carries no token, so holding this link grants
            // nothing and the submitted password is never applied to an account the caller may not own.
            pending = PendingRegistrationIntent.Notify(
                submission.Id,
                intent.Email,
                intent.LegalName,
                intent.Cuit,
                requestLanguage.Language,
                now,
                expiresAt);
            outbox = OutboxMessage.Create(IntentSignInNoticeMessageType, JsonSerializer.Serialize(new IntentEnvelope(pending.Id)), now);
        }

        context.PendingRegistrationIntents.Add(pending);
        context.OutboxMessages.Add(outbox);
        context.AuditEvents.Add(AuditEvent.CreateRegistrationIntentRecorded(Correlation(submission.CanonicalKey), now));
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<Result> CompleteSubmissionAsync(RegistrationSubmission submission, RegistrationSubmissionOutcome outcome, CancellationToken cancellationToken)
    {
        context.RegistrationSubmissions.Attach(submission);
        submission.Complete(outcome, timeProvider.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);
        return ResultFor(outcome);
    }

    private static Result Replay(RegistrationSubmission submission) => submission.Outcome is { } outcome
        ? ResultFor(outcome)
        : Result.Failure(IdentityAccessErrors.RegistrationConflict());

    private static Result ResultFor(RegistrationSubmissionOutcome outcome) => outcome switch
    {
        RegistrationSubmissionOutcome.Accepted => Result.Success(),
        RegistrationSubmissionOutcome.RegistrationConflict => Result.Failure(IdentityAccessErrors.RegistrationConflict()),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unsupported registration outcome.")
    };

    private bool TryNormalize(RegisterOrganizationCommand request, out RegistrationIntent intent)
    {
        intent = default;
        try
        {
            if (request.LegalName is null || request.Cuit is null) return false;
            if (request.LegalName.Length > 256 || request.Cuit.Length > 32) return false;

            string email;
            if (session.IdentityId is null)
            {
                if (request.Email is null || request.Password is null) return false;
                if (!TryNormalizeEmail(request.Email, out email)) return false;
                if (request.Password.Length > 256 || string.IsNullOrWhiteSpace(request.Password)) return false;
            }
            else
            {
                if (!TryNormalizeEmail(session.Email!, out email)) return false;
                if (!string.IsNullOrWhiteSpace(request.Email))
                {
                    if (!TryNormalizeEmail(request.Email, out var submittedEmail) ||
                        !string.Equals(submittedEmail, email, StringComparison.Ordinal)) return false;
                }
            }

            intent = new RegistrationIntent(email, request.LegalName.Trim(), NormalizedCuit.From(request.Cuit));
            return !string.IsNullOrWhiteSpace(intent.LegalName);
        }
        catch (ArgumentException) { return false; }
    }

    private static bool TryNormalizeEmail(string email, out string normalizedEmail)
    {
        if (email.Length > 256)
        {
            normalizedEmail = string.Empty;
            return false;
        }

        normalizedEmail = email.Trim().ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(normalizedEmail) && normalizedEmail.Contains('@');
    }

    private static string CanonicalKey(string scope, RegistrationIntent intent) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}|{intent.Email}|{intent.LegalName.ToUpperInvariant()}|{intent.Cuit.Value}")));
    private static string Correlation(string canonicalKey) => $"registration-{canonicalKey[..16].ToLowerInvariant()}";
    private readonly record struct RegistrationIntent(string Email, string LegalName, NormalizedCuit Cuit);
    private sealed record ConfirmationEnvelope(Guid IdentityId, Guid TenantId, Guid MembershipId);
}
