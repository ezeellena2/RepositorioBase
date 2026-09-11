using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.People;

namespace CleanArchitecture.Application.IdentityAccess.People.RegisterPersonal;

/// <summary>
/// The unproved half of a newcomer's Personal signup. It is the organization handler's anonymous branch, applied to
/// a person instead of a company: one intent, one message, one tenantless audit event, and no exclusive claim of any
/// kind — not the address, and not the documentary identity, whose fingerprints are written only by the proof
/// (IA-REQ-003, IA-REQ-048).
/// </summary>
public sealed class RegisterPersonalCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IRegistrationIdempotencyStore idempotencyStore,
    IIdentityAccountService identities,
    IIdentityDocumentProtector documents,
    IValidatedOptionalSession session,
    ISecureTokenGenerator tokens,
    ITokenHasher tokenHasher,
    IOutboxSecretWriter secretWriter,
    IRequestLanguage requestLanguage,
    TimeProvider timeProvider) : IRequestHandler<RegisterPersonalCommand, Result>
{
    /// <summary>Carries the intent's confirmation token to the address that must prove it owns itself.</summary>
    public const string IntentConfirmationMessageType = "identity.personal.confirmation.requested";

    /// <summary>The tokenless notice an address that already has an account receives instead.</summary>
    public const string IntentSignInNoticeMessageType = "identity.personal.signin.notice.requested";

    private static readonly TimeSpan IntentWindow = TimeSpan.FromHours(24);

    /// <summary>Named only by its identifier: the payload is stored in the clear and holds no address and no document.</summary>
    public sealed record IntentEnvelope(Guid IntentId);

    public async Task<Result> Handle(RegisterPersonalCommand request, CancellationToken cancellationToken)
    {
        if (!TryNormalize(request, out var intent)) return Result.Failure(IdentityAccessErrors.InvalidRegistration());
        if (session.IsInvalid || session.IdentityId.HasValue != (session.Email is not null))
            return Result.Failure(IdentityAccessErrors.InvalidSession());

        // Somebody already signed in has a route that needs no mailed token, and using this one would mean issuing
        // a second credential to an account that already has one. It is refused rather than quietly redirected.
        if (session.IdentityId is not null) return Result.Failure(IdentityAccessErrors.InvalidRegistration());

        // Password policy depends on the submitted password alone, so it is decided before any address is looked
        // up: validating it only for a free address would make a weak password answer differently for a taken one.
        if (!(await identities.ValidatePasswordAsync(request.Password, cancellationToken)).IsValid)
        {
            return Result.Failure(IdentityAccessErrors.InvalidRegistration());
        }

        return await transaction.ExecuteAsync(async ct =>
        {
            var canonicalKey = CanonicalKey(intent);
            var claim = await idempotencyStore.TryClaimAsync(canonicalKey, ct);
            if (!claim.IsOwner) return Replay(claim.Submission);

            await InitiateAsync(request, intent, claim.Submission, ct);
            return await CompleteSubmissionAsync(claim.Submission, RegistrationSubmissionOutcome.Accepted, ct);
        }, cancellationToken);
    }

    /// <summary>
    /// Writes the same number of rows whatever the system knows about the address, and does the same work either
    /// way: the password is hashed and the document is sealed before the address is looked up, so neither the answer
    /// nor the time it took varies with what is already there.
    /// </summary>
    private async Task InitiateAsync(RegisterPersonalCommand request, PersonalIntent intent, RegistrationSubmission submission, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(IntentWindow);
        var passwordHash = identities.HashPassword(request.Password);
        var ciphertext = documents.Protect(intent.Document);
        var existing = await identities.FindByEmailAsync(intent.Email, cancellationToken);

        PendingPersonalIntent pending;
        OutboxMessage outbox;
        if (existing is null)
        {
            pending = PendingPersonalIntent.Open(
                submission.Id,
                intent.Email,
                intent.FullName,
                intent.DisplayName,
                ciphertext,
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
            // The address owner is told they can sign in. Nothing of what the caller submitted survives: no token,
            // no password applied to an account they may not own, and no trace of the document they typed.
            pending = PendingPersonalIntent.Notify(
                submission.Id,
                intent.Email,
                intent.FullName,
                intent.DisplayName,
                requestLanguage.Language,
                now,
                expiresAt);
            outbox = OutboxMessage.Create(IntentSignInNoticeMessageType, JsonSerializer.Serialize(new IntentEnvelope(pending.Id)), now);
        }

        context.PendingPersonalIntents.Add(pending);
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
        : Result.Failure(IdentityAccessErrors.PersonalRegistrationConflict());

    private static Result ResultFor(RegistrationSubmissionOutcome outcome) => outcome switch
    {
        RegistrationSubmissionOutcome.Accepted => Result.Success(),
        RegistrationSubmissionOutcome.RegistrationConflict => Result.Failure(IdentityAccessErrors.PersonalRegistrationConflict()),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unsupported registration outcome.")
    };

    private static bool TryNormalize(RegisterPersonalCommand request, out PersonalIntent intent)
    {
        intent = default;
        try
        {
            if (request.Email is null || request.Password is null || request.FullName is null || request.DisplayName is null || request.DocumentNumber is null) return false;
            if (request.Email.Length > 256 || request.Password.Length > 256 || request.FullName.Length > 200 || request.DisplayName.Length > 60 || request.DocumentNumber.Length > 32) return false;
            var email = request.Email.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || string.IsNullOrWhiteSpace(request.Password)) return false;
            var document = NormalizedDocument.From(IdentityDocumentCountry.AR, IdentityDocumentKind.DNI, request.DocumentNumber);
            intent = new PersonalIntent(email, request.FullName.Trim(), request.DisplayName.Trim(), document);
            return !string.IsNullOrWhiteSpace(intent.FullName) && !string.IsNullOrWhiteSpace(intent.DisplayName);
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>
    /// The document is part of the key, so two different people submitting one address are two submissions rather
    /// than one replay. It is hashed with everything else and never stored in the clear.
    /// </summary>
    private static string CanonicalKey(PersonalIntent intent) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"personal|{intent.Email}|{intent.FullName.ToUpperInvariant()}|{intent.DisplayName.ToUpperInvariant()}|{intent.Document.Canonical}")));

    private static string Correlation(string canonicalKey) => $"personal-{canonicalKey[..16].ToLowerInvariant()}";

    private readonly record struct PersonalIntent(string Email, string FullName, string DisplayName, NormalizedDocument Document);
}
