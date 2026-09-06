using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.IdentityAccess.Organizations;

/// <summary>
/// What an anonymous registration leaves behind before anybody has proved control of the address (IA-REQ-048).
/// <para>
/// It is deliberately not a reservation. It holds the submission so that spending the mailed token can finish it,
/// and it claims nothing: no CUIT, no identity, no tenant. That is the whole point — a request nobody has proved
/// must not be able to refuse, delay or change the answer given to anyone else, and a row that reserved a CUIT
/// would do exactly that while telling the prober whether the address it named already had an account.
/// </para>
/// <para>
/// The password arrives once, at initiation, and is kept only as the irreversible hash the configured hasher
/// produced. Finalization creates the identity from that hash, so the plaintext is never stored and never
/// travels twice.
/// </para>
/// </summary>
public sealed class PendingRegistrationIntent : BaseEntity<Guid>
{
    private PendingRegistrationIntent() { }

    /// <summary>The submission whose canonical key made this initiation idempotent.</summary>
    public Guid SubmissionId { get; private set; }

    public string NormalizedEmail { get; private set; } = string.Empty;

    public string LegalName { get; private set; } = string.Empty;

    public NormalizedCuit Cuit { get; private set; }

    /// <summary>Empty for an address that already has an account: that branch creates no identity and needs none.</summary>
    public string PasswordHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public PendingRegistrationIntentOutcome? Outcome { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// Records an initiation for an address with no account. The token that finalizes it lives, hashed and sealed,
    /// in the referenced <c>OutboxSecret</c> exactly as every other mailed token does; nothing about it is here.
    /// </summary>
    public static PendingRegistrationIntent Open(
        Guid submissionId,
        string normalizedEmail,
        string legalName,
        NormalizedCuit cuit,
        string passwordHash,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        var intent = Create(submissionId, normalizedEmail, legalName, cuit, now, expiresAt);
        intent.PasswordHash = passwordHash;
        return intent;
    }

    /// <summary>
    /// Records an initiation for an address that already has an account. It is terminal on arrival: the address
    /// owner is told they can sign in, no token is minted, and there is nothing for anyone to finalize. It exists
    /// so that this branch writes the same number of rows as the other one.
    /// </summary>
    public static PendingRegistrationIntent Notify(
        Guid submissionId,
        string normalizedEmail,
        string legalName,
        NormalizedCuit cuit,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        var intent = Create(submissionId, normalizedEmail, legalName, cuit, now, expiresAt);
        intent.Outcome = PendingRegistrationIntentOutcome.Notified;
        intent.CompletedAt = now;
        return intent;
    }

    /// <summary>
    /// Settles the intent. It is called once: the first spend of the token decides the outcome and every later
    /// spend reads it back, which is what makes finalization idempotent on the token rather than on a retry count.
    /// </summary>
    public void Complete(PendingRegistrationIntentOutcome outcome, DateTimeOffset now)
    {
        if (Outcome is not null) throw new InvalidOperationException("A registration intent cannot be completed twice.");
        if (now < CreatedAt) throw new ArgumentOutOfRangeException(nameof(now));
        Outcome = outcome;
        CompletedAt = now;
    }

    public bool IsPendingAt(DateTimeOffset now) => Outcome is null && now < ExpiresAt;

    private static PendingRegistrationIntent Create(
        Guid submissionId,
        string normalizedEmail,
        string legalName,
        NormalizedCuit cuit,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        if (submissionId == Guid.Empty) throw new ArgumentException("A registration intent belongs to a submission.", nameof(submissionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(legalName);
        if (expiresAt <= now) throw new ArgumentOutOfRangeException(nameof(expiresAt));

        return new PendingRegistrationIntent
        {
            Id = Guid.NewGuid(),
            SubmissionId = submissionId,
            NormalizedEmail = normalizedEmail,
            LegalName = legalName,
            Cuit = cuit,
            CreatedAt = now,
            ExpiresAt = expiresAt
        };
    }
}
