using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.IdentityAccess.Organizations;

namespace CleanArchitecture.Domain.IdentityAccess.People;

/// <summary>
/// What an anonymous Personal signup leaves behind before anybody has proved control of the address (IA-REQ-048).
/// <para>
/// It is the organization intent's sibling and keeps the same promise: it reserves nothing. The document arrives
/// already sealed and is held as ciphertext alone — no fingerprint row is written, so the documentary identity is
/// not claimed and nobody else is refused by it. Only spending the mailed token turns this into a Personal context,
/// and that is where the fingerprints, the tenant and the profile appear.
/// </para>
/// <para>
/// It settles with <see cref="PendingRegistrationIntentOutcome"/> rather than an enum of its own, because the four
/// ways a registration can end are the same four here and two parallel vocabularies would drift.
/// </para>
/// </summary>
public sealed class PendingPersonalIntent : BaseEntity<Guid>
{
    private PendingPersonalIntent() { }

    /// <summary>The submission whose canonical key made this initiation idempotent.</summary>
    public Guid SubmissionId { get; private set; }

    public string NormalizedEmail { get; private set; } = string.Empty;

    /// <summary>The anonymous request's supported language, captured before any account exists.</summary>
    public string? Language { get; private set; }

    public string FullName { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>The sealed canonical tuple. It is not a claim: no fingerprint exists until the address is proved.</summary>
    public string DocumentCiphertext { get; private set; } = string.Empty;

    /// <summary>Empty for an address that already has an account: that branch creates no identity and needs none.</summary>
    public string PasswordHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public PendingRegistrationIntentOutcome? Outcome { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public static PendingPersonalIntent Open(
        Guid submissionId,
        string normalizedEmail,
        string fullName,
        string displayName,
        string documentCiphertext,
        string passwordHash,
        DateTimeOffset now,
        DateTimeOffset expiresAt) =>
        Open(submissionId, normalizedEmail, fullName, displayName, documentCiphertext, passwordHash, "en", now, expiresAt);

    public static PendingPersonalIntent Open(
        Guid submissionId,
        string normalizedEmail,
        string fullName,
        string displayName,
        string documentCiphertext,
        string passwordHash,
        string language,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentCiphertext);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        var intent = Create(submissionId, normalizedEmail, fullName, displayName, language, now, expiresAt);
        intent.DocumentCiphertext = documentCiphertext;
        intent.PasswordHash = passwordHash;
        return intent;
    }

    /// <summary>
    /// Records an initiation for an address that already has an account. It is terminal on arrival and holds no
    /// document at all: nothing about the submitted number may survive a request the caller never proved.
    /// </summary>
    public static PendingPersonalIntent Notify(
        Guid submissionId,
        string normalizedEmail,
        string fullName,
        string displayName,
        DateTimeOffset now,
        DateTimeOffset expiresAt) =>
        Notify(submissionId, normalizedEmail, fullName, displayName, "en", now, expiresAt);

    public static PendingPersonalIntent Notify(
        Guid submissionId,
        string normalizedEmail,
        string fullName,
        string displayName,
        string language,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        var intent = Create(submissionId, normalizedEmail, fullName, displayName, language, now, expiresAt);
        intent.Outcome = PendingRegistrationIntentOutcome.Notified;
        intent.CompletedAt = now;
        return intent;
    }

    public void Complete(PendingRegistrationIntentOutcome outcome, DateTimeOffset now)
    {
        if (Outcome is not null) throw new InvalidOperationException("A personal intent cannot be completed twice.");
        if (now < CreatedAt) throw new ArgumentOutOfRangeException(nameof(now));
        Outcome = outcome;
        CompletedAt = now;
    }

    public bool IsPendingAt(DateTimeOffset now) => Outcome is null && now < ExpiresAt;

    private static PendingPersonalIntent Create(
        Guid submissionId,
        string normalizedEmail,
        string fullName,
        string displayName,
        string language,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        if (submissionId == Guid.Empty) throw new ArgumentException("A personal intent belongs to a submission.", nameof(submissionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        if (expiresAt <= now) throw new ArgumentOutOfRangeException(nameof(expiresAt));

        return new PendingPersonalIntent
        {
            Id = Guid.NewGuid(),
            SubmissionId = submissionId,
            NormalizedEmail = normalizedEmail,
            Language = language,
            FullName = fullName,
            DisplayName = displayName,
            CreatedAt = now,
            ExpiresAt = expiresAt
        };
    }
}
